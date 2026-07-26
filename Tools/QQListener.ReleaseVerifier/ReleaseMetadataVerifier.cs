using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QQListener.ReleaseVerifier;

public static class ReleaseMetadataVerifier
{
    private const string TargetFramework = "net8.0-windows10.0.19041.0";
    private const string ExpectedReleaseVersion = "1.1.0.0";
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxCipxBytes = 256L * 1024 * 1024;
    private const long MaxChecksumsBytes = 1024L * 1024;
    private const int MaxArchiveEntries = 512;
    private const long MaxEntryBytes = 128L * 1024 * 1024;
    private const long MaxExtractedBytes = 256L * 1024 * 1024;
    // 32 MiB is intentionally far above a legitimate 65,000-byte deep-path
    // directory record while bounding name/extra-field materialization for 512 entries.
    private const ulong MaxCentralDirectoryBytes = 32UL * 1024 * 1024;
    private const uint ClassicEocdSignature = 0x06054b50;
    private const uint Zip64EocdSignature = 0x06064b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const int ClassicEocdLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EocdMinimumLength = 56;
    private static readonly string[] RequiredRootFiles =
        ["manifest.yml", "QQListener.dll", "README.md", "CHANGELOG.md", "icon.png"];
    private static readonly Regex CanonicalVersionPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ChecksumMarkerPattern = new(
        @"<!--\s*CLASSISLAND_PKG_MD5\s+(\{.*?\})\s*-->",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    internal static Func<string> TemporaryDirectoryFactory { get; set; } = CreateTemporaryDirectory;

    internal static ArchiveResourceBudget CreateArchiveResourceBudget() =>
        new(MaxExtractedBytes);

    public static VerificationResult CompareVersions(
        string manifestVersion,
        string projectVersion,
        string assemblyVersion,
        string fileVersion,
        string? tagVersion)
    {
        var errors = new List<string>();
        ValidateCanonical(errors, "manifest", manifestVersion);
        ValidateCanonical(errors, "project", projectVersion);
        ValidateCanonical(errors, "assembly", assemblyVersion);
        ValidateCanonical(errors, "file", fileVersion);
        if (tagVersion is not null)
        {
            ValidateCanonical(errors, "tag", tagVersion);
        }

        if (errors.Count > 0) return new VerificationResult(errors);

        AddMismatch(errors, "project", manifestVersion, projectVersion);
        AddMismatch(errors, "assembly", manifestVersion, assemblyVersion);
        AddMismatch(errors, "file", manifestVersion, fileVersion);
        if (tagVersion is not null)
        {
            AddMismatch(errors, "tag", manifestVersion, tagVersion);
        }

        return new VerificationResult(errors);
    }

    public static VerificationResult VerifyProject(
        string rootPath,
        string configuration,
        string? tagVersion) =>
        VerificationResult.Combine(
            VerifyMetadata(rootPath, configuration, tagVersion),
            VerifyPackage(rootPath, configuration));

    public static VerificationResult VerifyMetadata(
        string rootPath,
        string configuration,
        string? tagVersion)
    {
        var errors = new List<string>();
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            errors.Add($"root directory '{root}' does not exist.");
            return new VerificationResult(errors);
        }

        string? manifestVersion = null;
        string? projectVersion = null;
        DllMetadata? dll = null;

        Try(errors, "manifest", () =>
        {
            var metadata = ReadManifest(Path.Combine(root, "manifest.yml"));
            manifestVersion = metadata.Version;
            if (!string.Equals(metadata.EntranceAssembly, "QQListener.dll", StringComparison.Ordinal))
            {
                errors.Add("manifest entranceAssembly must be exactly 'QQListener.dll'.");
            }
        });
        Try(errors, "project", () =>
            projectVersion = ReadProjectVersion(Path.Combine(root, "Directory.Build.props")));
        Try(errors, "build DLL", () =>
            dll = ReadDll(Path.Combine(
                root,
                "bin",
                configuration,
                TargetFramework,
                "QQListener.dll")));

        if (manifestVersion is not null && projectVersion is not null && dll is not null)
        {
            errors.AddRange(CompareVersions(
                manifestVersion,
                projectVersion,
                dll.AssemblyVersion,
                dll.FileVersion,
                tagVersion).Errors);
            if (!string.Equals(dll.SimpleName, "QQListener", StringComparison.Ordinal))
            {
                errors.Add("build DLL assembly name is not 'QQListener'.");
            }
        }
        else if (tagVersion is not null)
        {
            ValidateCanonical(errors, "tag", tagVersion);
        }

        return new VerificationResult(errors);
    }

    public static VerificationResult VerifyPackage(string rootPath, string configuration)
    {
        var errors = new List<string>();
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            errors.Add($"root directory '{root}' does not exist.");
            return new VerificationResult(errors);
        }

        var cipxPath = Path.Combine(root, "cipx", "QQListener.cipx");
        var checksumsPath = Path.Combine(root, "cipx", "checksums.md");
        Try(errors, "package", () =>
        {
            if (!File.Exists(cipxPath))
            {
                throw new FileNotFoundException("CIPX file is missing.", cipxPath);
            }

            if (!File.Exists(checksumsPath))
            {
                throw new FileNotFoundException("checksums.md is missing.", checksumsPath);
            }

            if (new FileInfo(cipxPath).Length > MaxCipxBytes)
            {
                throw new InvalidDataException("CIPX file is too large.");
            }

            if (new FileInfo(checksumsPath).Length > MaxChecksumsBytes)
            {
                throw new InvalidDataException("checksums.md is too large.");
            }
        });
        if (errors.Count > 0)
        {
            return new VerificationResult(errors);
        }

        Try(errors, "checksum", () => VerifyChecksum(cipxPath, checksumsPath));
        if (errors.Count > 0)
        {
            return new VerificationResult(errors);
        }

        string? projectVersion = null;
        Try(errors, "project", () =>
            projectVersion = ReadProjectVersion(Path.Combine(root, "Directory.Build.props")));

        PackageMetadata? package = null;
        Try(errors, "archive", () => package = ReadPackage(cipxPath, errors));

        if (projectVersion is not null && package is not null)
        {
            errors.AddRange(CompareVersions(
                package.Manifest.Version,
                projectVersion,
                package.Dll.AssemblyVersion,
                package.Dll.FileVersion,
                tagVersion: null).Errors);
            RequireExpectedReleaseVersion(errors, "manifest", package.Manifest.Version);
            RequireExpectedReleaseVersion(errors, "project", projectVersion);
            RequireExpectedReleaseVersion(errors, "assembly", package.Dll.AssemblyVersion);
            RequireExpectedReleaseVersion(errors, "file", package.Dll.FileVersion);
            if (!string.Equals(package.Manifest.EntranceAssembly, "QQListener.dll", StringComparison.Ordinal))
            {
                errors.Add("archive manifest entranceAssembly must be exactly 'QQListener.dll'.");
            }

            if (!string.Equals(package.Dll.SimpleName, "QQListener", StringComparison.Ordinal))
            {
                errors.Add("archive DLL assembly name is not 'QQListener'.");
            }
        }

        return new VerificationResult(errors);
    }

    public static IReadOnlyDictionary<string, string> ParseChecksums(string markdown)
    {
        var matches = ChecksumMarkerPattern.Matches(markdown);
        if (matches.Count != 1)
        {
            throw new FormatException("checksums.md must contain exactly one CLASSISLAND_PKG_MD5 marker.");
        }

        Dictionary<string, string>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string>>(matches[0].Groups[1].Value);
        }
        catch (JsonException exception)
        {
            throw new FormatException("CLASSISLAND_PKG_MD5 marker contains invalid JSON.", exception);
        }

        if (values is null
            || !values.TryGetValue("QQListener.cipx", out var hash)
            || !Regex.IsMatch(hash, "^[0-9A-Fa-f]{32}$", RegexOptions.CultureInvariant))
        {
            throw new FormatException(
                "CLASSISLAND_PKG_MD5 must contain QQListener.cipx with a 32-character hexadecimal hash.");
        }

        return values;
    }

    public static string ReadProjectVersion(string path)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var elements = document
            .Descendants()
            .Where(element => element.Name.LocalName == "QQListenerVersion")
            .ToArray();
        if (elements.Length != 1)
        {
            throw new FormatException("project must contain exactly one QQListenerVersion element.");
        }

        if (elements[0].Attribute("Condition") is not null
            || elements[0].Ancestors().Any(element => element.Attribute("Condition") is not null))
        {
            throw new FormatException("project QQListenerVersion must not be conditional.");
        }

        var value = elements[0].Value;
        EnsureCanonical("project", value);
        return value;
    }

    private static PackageMetadata ReadPackage(string cipxPath, List<string> errors)
    {
        if (!File.Exists(cipxPath))
        {
            throw new FileNotFoundException("CIPX file is missing.", cipxPath);
        }

        var directoryMetadata = AuditArchiveDirectoryMetadata(cipxPath);
        using var archive = ZipFile.OpenRead(cipxPath);
        var auditErrorCount = errors.Count;
        if (archive.Entries.Count > MaxArchiveEntries)
        {
            errors.Add($"archive contains more than {MaxArchiveEntries} entries.");
            throw new FormatException("archive directory audit failed.");
        }

        if ((ulong)archive.Entries.Count != directoryMetadata.EntryCount)
        {
            errors.Add("archive entry count does not match its ZIP metadata.");
            throw new FormatException("archive directory audit failed.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var safePaths = new List<(string Path, string ComparisonKey, string SortKey)>();
        var declaredBudget = CreateArchiveResourceBudget();
        var requiredEntries = RequiredRootFiles.ToDictionary(
            CreateKnownSafeArchiveComparisonKey,
            name => (ExactName: name, Entry: (ZipArchiveEntry?)null),
            StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!TryCreateArchiveComparisonKey(name, out var comparisonKey))
            {
                errors.Add(
                    "archive contains an entry that is not a safe relative path or Windows-portable name.");
                continue;
            }

            var entryLimit = name == "manifest.yml" ? MaxManifestBytes : MaxEntryBytes;
            var budgetError = declaredBudget.AuditDeclared(name, entry.Length, entryLimit);
            if (budgetError is not null)
            {
                errors.Add(budgetError);
            }

            if (!seen.Add(comparisonKey))
            {
                errors.Add($"archive entry '{name}' is duplicated by canonical case-insensitive name.");
            }

            safePaths.Add((name, comparisonKey, comparisonKey + "/"));

            var basenameKey = comparisonKey[(comparisonKey.LastIndexOf('/') + 1)..];
            if (requiredEntries.TryGetValue(basenameKey, out var reservedEntry)
                && !name.Equals(reservedEntry.ExactName, StringComparison.Ordinal))
            {
                errors.Add($"archive reserved entry '{name}' must use its exact root-level name.");
            }

            if (comparisonKey.EndsWith(".CIPX", StringComparison.Ordinal)
                || basenameKey.Equals("CHECKSUMS.MD", StringComparison.Ordinal)
                || comparisonKey.EndsWith(".MD5", StringComparison.Ordinal)
                || comparisonKey.EndsWith(".MD5SUM", StringComparison.Ordinal))
            {
                errors.Add($"archive entry '{name}' is a nested or generated artifact.");
            }

            if (requiredEntries.TryGetValue(comparisonKey, out var requiredEntry)
                && name.Equals(requiredEntry.ExactName, StringComparison.Ordinal))
            {
                requiredEntries[comparisonKey] = (requiredEntry.ExactName, entry);
            }
        }

        safePaths.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.SortKey, right.SortKey));
        for (var index = 1; index < safePaths.Count; index++)
        {
            var previous = safePaths[index - 1];
            var current = safePaths[index];
            if (current.ComparisonKey.Length > previous.ComparisonKey.Length
                && current.ComparisonKey.StartsWith(previous.ComparisonKey, StringComparison.Ordinal)
                && current.ComparisonKey[previous.ComparisonKey.Length] == '/')
            {
                errors.Add(
                    $"archive entry '{current.Path}' has a canonical file-directory collision.");
            }
        }

        foreach (var required in requiredEntries)
        {
            if (required.Value.Entry is null)
            {
                errors.Add($"archive is missing root {required.Value.ExactName}.");
            }
        }

        if (errors.Count != auditErrorCount)
        {
            throw new FormatException("archive directory audit failed.");
        }

        string? tempDirectory = null;
        try
        {
            tempDirectory = TemporaryDirectoryFactory();
            var extractionRoot = Path.GetFullPath(tempDirectory);
            if (!Directory.Exists(extractionRoot))
            {
                throw new IOException("temporary extraction directory was not created.");
            }

            var rootPrefix = extractionRoot.TrimEnd(
                                 Path.DirectorySeparatorChar,
                                 Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var extractionBudget = CreateArchiveResourceBudget();
            foreach (var entry in archive.Entries)
            {
                var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, relativePath));
                if (!destinationPath.StartsWith(rootPrefix, pathComparison))
                {
                    throw new InvalidDataException("archive extraction path escaped the temporary directory.");
                }

                var parentDirectory = Path.GetDirectoryName(destinationPath)
                                      ?? throw new InvalidDataException("archive extraction path has no parent directory.");
                Directory.CreateDirectory(parentDirectory);
                using var input = entry.Open();
                using var output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                var entryLimit = entry.FullName == "manifest.yml"
                    ? MaxManifestBytes
                    : MaxEntryBytes;
                extractionBudget.CopyEntry(input, output, entry.FullName, entryLimit);
            }

            foreach (var requiredName in RequiredRootFiles)
            {
                EnsureLeafFile(Path.Combine(extractionRoot, requiredName), requiredName);
            }

            var manifest = ReadManifest(Path.Combine(extractionRoot, "manifest.yml"));
            var dll = ReadDll(Path.Combine(extractionRoot, "QQListener.dll"));
            return new PackageMetadata(manifest, dll);
        }
        finally
        {
            if (tempDirectory is not null && Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("temporary directory cleanup failed.", exception);
                }
            }
        }
    }

    private static void EnsureLeafFile(string path, string name)
    {
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            throw new FormatException($"archive root {name} must be a leaf file.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = stream.ReadByte();
    }

    private static ManifestMetadata ReadManifest(string path) =>
        ParseManifest(File.ReadAllText(path));

    private static ManifestMetadata ParseManifest(string yaml)
    {
        Dictionary<string, object?> values;
        try
        {
            values = new DeserializerBuilder()
                .WithDuplicateKeyChecking()
                .Build()
                .Deserialize<Dictionary<string, object?>>(yaml)
                ?? throw new FormatException("manifest is empty.");
        }
        catch (YamlException exception)
        {
            throw new FormatException("manifest YAML is invalid or contains duplicate keys.", exception);
        }

        if (!values.TryGetValue("version", out var versionValue)
            || versionValue is not string version)
        {
            throw new FormatException("manifest must contain one scalar version value.");
        }

        EnsureCanonical("manifest", version);
        var entrance = values.TryGetValue("entranceAssembly", out var entranceValue)
            ? entranceValue as string ?? ""
            : "";
        return new ManifestMetadata(version, entrance);
    }

    private static DllMetadata ReadDll(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("DLL file is missing.", path);
        var assembly = AssemblyName.GetAssemblyName(path);
        var assemblyVersion = assembly.Version?.ToString(4)
                              ?? throw new FormatException("DLL assembly version is missing.");
        var fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion
                          ?? throw new FormatException("DLL file version is missing.");
        EnsureCanonical("assembly", assemblyVersion);
        EnsureCanonical("file", fileVersion);
        return new DllMetadata(assembly.Name ?? "", assemblyVersion, fileVersion);
    }

    private static void VerifyChecksum(string cipxPath, string checksumsPath)
    {
        if (!File.Exists(cipxPath)) throw new FileNotFoundException("CIPX file is missing.", cipxPath);
        if (!File.Exists(checksumsPath)) throw new FileNotFoundException("checksums.md is missing.", checksumsPath);
        var values = ParseChecksums(File.ReadAllText(checksumsPath));
        var filename = Path.GetFileName(cipxPath);
        if (!values.TryGetValue(filename, out var expected))
        {
            throw new FormatException($"checksum marker does not contain exact filename '{filename}'.");
        }

        using var stream = new FileStream(cipxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(MD5.HashData(stream));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"checksum mismatch for '{filename}'.");
        }
    }

    private static bool TryCreateArchiveComparisonKey(string name, out string key)
    {
        key = "";
        if (string.IsNullOrEmpty(name)
            || name.Contains('\\')
            || name.Contains(':')
            || Path.IsPathRooted(name))
        {
            return false;
        }

        var segments = name.Split('/');
        var comparisonSegments = new string[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.EndsWith('.')
                || segment.EndsWith(' ')
                || segment.Any(character =>
                    character <= '\u001f'
                    || character == '\u007f'
                    || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'))
            {
                return false;
            }

            string comparisonSegment;
            try
            {
                if (!segment.IsNormalized(NormalizationForm.FormC))
                {
                    return false;
                }

                comparisonSegment = segment
                    .Normalize(NormalizationForm.FormC)
                    .ToUpperInvariant()
                    .Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var extensionIndex = comparisonSegment.IndexOf('.');
            var deviceStem = extensionIndex >= 0
                ? comparisonSegment[..extensionIndex]
                : comparisonSegment;
            if (IsWindowsDeviceStem(deviceStem))
            {
                return false;
            }

            comparisonSegments[index] = comparisonSegment;
        }

        key = string.Join('/', comparisonSegments);
        return true;
    }

    private static string CreateKnownSafeArchiveComparisonKey(string name)
    {
        if (!TryCreateArchiveComparisonKey(name, out var key))
        {
            throw new InvalidOperationException("required archive root name is not Windows-portable.");
        }

        return key;
    }

    private static bool IsWindowsDeviceStem(string stem)
    {
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
        {
            return true;
        }

        return stem.Length == 4
               && (stem.StartsWith("COM", StringComparison.Ordinal)
                   || stem.StartsWith("LPT", StringComparison.Ordinal))
               && stem[3] is >= '1' and <= '9';
    }

    private static ArchiveDirectoryMetadata AuditArchiveDirectoryMetadata(string cipxPath)
    {
        using var stream = new FileStream(cipxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var eocd = ReadClassicEocd(stream);
        if (eocd.DiskNumber != 0
            || eocd.CentralDirectoryDiskNumber != 0
            || eocd.EntriesOnDisk != eocd.TotalEntries)
        {
            throw new InvalidDataException("archive ZIP metadata declares a multi-disk archive.");
        }

        var usesZip64 = eocd.EntriesOnDisk == ushort.MaxValue
                        || eocd.TotalEntries == ushort.MaxValue
                        || eocd.CentralDirectorySize == uint.MaxValue
                        || eocd.CentralDirectoryOffset == uint.MaxValue;
        if (usesZip64)
        {
            return ReadZip64DirectoryMetadata(stream, eocd);
        }

        var entryCount = (ulong)eocd.TotalEntries;
        var centralDirectorySize = (ulong)eocd.CentralDirectorySize;
        var centralDirectoryOffset = (ulong)eocd.CentralDirectoryOffset;
        ValidateArchiveDirectoryBounds(
            entryCount,
            centralDirectorySize,
            centralDirectoryOffset,
            (ulong)eocd.Offset);
        return new ArchiveDirectoryMetadata(entryCount);
    }

    private static ClassicEocdMetadata ReadClassicEocd(FileStream stream)
    {
        if (stream.Length < ClassicEocdLength)
        {
            throw new InvalidDataException("archive ZIP metadata is missing or truncated.");
        }

        var searchLength = (int)Math.Min(
            stream.Length,
            ClassicEocdLength + (long)ushort.MaxValue);
        var tail = new byte[searchLength];
        var tailOffset = stream.Length - searchLength;
        ReadExactlyAt(stream, tailOffset, tail, "archive ZIP metadata is truncated.");

        for (var index = tail.Length - ClassicEocdLength; index >= 0; index--)
        {
            var candidate = tail.AsSpan(index);
            if (BinaryPrimitives.ReadUInt32LittleEndian(candidate) != ClassicEocdSignature)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(candidate[20..]);
            if (index + ClassicEocdLength + commentLength != tail.Length)
            {
                continue;
            }

            return new ClassicEocdMetadata(
                tailOffset + index,
                BinaryPrimitives.ReadUInt16LittleEndian(candidate[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(candidate[6..]),
                BinaryPrimitives.ReadUInt16LittleEndian(candidate[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(candidate[10..]),
                BinaryPrimitives.ReadUInt32LittleEndian(candidate[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(candidate[16..]));
        }

        throw new InvalidDataException("archive ZIP metadata is missing or truncated.");
    }

    private static ArchiveDirectoryMetadata ReadZip64DirectoryMetadata(
        FileStream stream,
        ClassicEocdMetadata classicEocd)
    {
        var locatorOffset = classicEocd.Offset - Zip64LocatorLength;
        if (locatorOffset < 0)
        {
            throw new InvalidDataException("archive ZIP64 locator is missing or truncated.");
        }

        Span<byte> locator = stackalloc byte[Zip64LocatorLength];
        ReadExactlyAt(
            stream,
            locatorOffset,
            locator,
            "archive ZIP64 locator is missing or truncated.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
        {
            throw new InvalidDataException("archive ZIP64 locator is missing or truncated.");
        }

        var recordDiskNumber = BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]);
        var recordOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        var totalDisks = BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]);
        if (recordDiskNumber != 0 || totalDisks != 1)
        {
            throw new InvalidDataException("archive ZIP metadata declares a multi-disk archive.");
        }

        if (recordOffset > long.MaxValue
            || recordOffset > (ulong)locatorOffset
            || (ulong)locatorOffset - recordOffset < Zip64EocdMinimumLength)
        {
            throw new InvalidDataException("archive ZIP64 record offset is out of range.");
        }

        Span<byte> record = stackalloc byte[Zip64EocdMinimumLength];
        ReadExactlyAt(
            stream,
            (long)recordOffset,
            record,
            "archive ZIP64 record is missing or truncated.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EocdSignature)
        {
            throw new InvalidDataException("archive ZIP64 record is malformed.");
        }

        var sizeAfterHeader = BinaryPrimitives.ReadUInt64LittleEndian(record[4..]);
        if (sizeAfterHeader < 44)
        {
            throw new InvalidDataException("archive ZIP64 record is malformed.");
        }

        var recordLength = CheckedAdd(12, sizeAfterHeader, "archive ZIP64 record length is out of range.");
        var recordEnd = CheckedAdd(
            recordOffset,
            recordLength,
            "archive ZIP64 record length is out of range.");
        if (recordEnd != (ulong)locatorOffset)
        {
            throw new InvalidDataException("archive ZIP64 record overlaps or is detached from its locator.");
        }

        var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        var centralDirectoryDiskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
        var entriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(record[24..]);
        var totalEntries = BinaryPrimitives.ReadUInt64LittleEndian(record[32..]);
        if (diskNumber != 0
            || centralDirectoryDiskNumber != 0
            || entriesOnDisk != totalEntries)
        {
            throw new InvalidDataException("archive ZIP metadata declares a multi-disk archive.");
        }

        var centralDirectorySize = BinaryPrimitives.ReadUInt64LittleEndian(record[40..]);
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[48..]);
        ValidateZip64ClassicFields(classicEocd, totalEntries, centralDirectorySize, centralDirectoryOffset);
        ValidateArchiveDirectoryBounds(
            totalEntries,
            centralDirectorySize,
            centralDirectoryOffset,
            recordOffset);
        return new ArchiveDirectoryMetadata(totalEntries);
    }

    private static void ValidateZip64ClassicFields(
        ClassicEocdMetadata classicEocd,
        ulong entryCount,
        ulong centralDirectorySize,
        ulong centralDirectoryOffset)
    {
        if ((classicEocd.TotalEntries != ushort.MaxValue
             && classicEocd.TotalEntries != entryCount)
            || (classicEocd.CentralDirectorySize != uint.MaxValue
                && classicEocd.CentralDirectorySize != centralDirectorySize)
            || (classicEocd.CentralDirectoryOffset != uint.MaxValue
                && classicEocd.CentralDirectoryOffset != centralDirectoryOffset))
        {
            throw new InvalidDataException("archive classic and ZIP64 metadata disagree.");
        }
    }

    private static void ValidateArchiveDirectoryBounds(
        ulong entryCount,
        ulong centralDirectorySize,
        ulong centralDirectoryOffset,
        ulong metadataOffset)
    {
        if (entryCount > MaxArchiveEntries)
        {
            throw new InvalidDataException(
                $"archive metadata declares more than {MaxArchiveEntries} entries.");
        }

        if (centralDirectorySize > MaxCentralDirectoryBytes)
        {
            throw new InvalidDataException(
                "archive central directory exceeds the 32 MiB safety limit.");
        }

        var centralDirectoryEnd = CheckedAdd(
            centralDirectoryOffset,
            centralDirectorySize,
            "archive central directory offset is out of range.");
        if (centralDirectoryOffset > metadataOffset || centralDirectoryEnd > metadataOffset)
        {
            throw new InvalidDataException("archive central directory overlaps ZIP metadata.");
        }
    }

    private static ulong CheckedAdd(ulong left, ulong right, string error)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(error, exception);
        }
    }

    private static void ReadExactlyAt(
        FileStream stream,
        long offset,
        Span<byte> destination,
        string error)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
        {
            throw new InvalidDataException(error);
        }

        stream.Position = offset;
        try
        {
            stream.ReadExactly(destination);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(error, exception);
        }
    }

    private static void ValidateCanonical(List<string> errors, string source, string value)
    {
        try
        {
            EnsureCanonical(source, value);
        }
        catch (FormatException exception)
        {
            errors.Add(exception.Message);
        }
    }

    private static void EnsureCanonical(string source, string value)
    {
        if (!CanonicalVersionPattern.IsMatch(value))
        {
            throw new FormatException(
                $"{source} version is not a canonical four-part version.");
        }

        var parts = value.Split('.');
        if (parts.Any(part => !int.TryParse(part, out _))
            || new System.Version(value).ToString(4) != value)
        {
            throw new FormatException(
                $"{source} version is not a canonical four-part version.");
        }
    }

    private static void RequireExpectedReleaseVersion(
        List<string> errors,
        string source,
        string value)
    {
        if (!string.Equals(value, ExpectedReleaseVersion, StringComparison.Ordinal))
        {
            errors.Add($"{source} version is not the required release version {ExpectedReleaseVersion}.");
        }
    }

    private static void AddMismatch(
        List<string> errors,
        string source,
        string expected,
        string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            errors.Add($"{source} version does not match manifest version.");
        }
    }

    private static void Try(List<string> errors, string category, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or FormatException
                                          or InvalidDataException
                                          or BadImageFormatException
                                          or XmlException)
        {
            errors.Add($"{category}: {exception.Message}");
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"QQListener.ReleaseVerifier.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record ManifestMetadata(string Version, string EntranceAssembly);
    private sealed record DllMetadata(string SimpleName, string AssemblyVersion, string FileVersion);
    private sealed record PackageMetadata(ManifestMetadata Manifest, DllMetadata Dll);
    private sealed record ArchiveDirectoryMetadata(ulong EntryCount);
    private sealed record ClassicEocdMetadata(
        long Offset,
        ushort DiskNumber,
        ushort CentralDirectoryDiskNumber,
        ushort EntriesOnDisk,
        ushort TotalEntries,
        uint CentralDirectorySize,
        uint CentralDirectoryOffset);
}

internal sealed class ArchiveResourceBudget(long totalLimit)
{
    private long _declaredBytes;
    private long _extractedBytes;

    internal string? AuditDeclared(string entryPath, long length, long entryLimit)
    {
        if (length > entryLimit)
        {
            return $"archive entry '{entryPath}' exceeds the allowed size.";
        }

        if (length > totalLimit - _declaredBytes)
        {
            return $"archive entry '{entryPath}' exceeds the total extraction budget.";
        }

        _declaredBytes += length;
        return null;
    }

    internal void CopyEntry(Stream input, Stream output, string entryPath, long entryLimit)
    {
        var buffer = new byte[81920];
        var entryBytes = 0L;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bytesRead > entryLimit - entryBytes
                || bytesRead > totalLimit - _extractedBytes)
            {
                throw new InvalidDataException(
                    $"archive entry '{entryPath}' exceeds the allowed extraction budget.");
            }

            output.Write(buffer, 0, bytesRead);
            entryBytes += bytesRead;
            _extractedBytes += bytesRead;
        }
    }
}
