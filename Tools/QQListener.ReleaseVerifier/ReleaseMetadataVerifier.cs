using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
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
    private const long MaxDllBytes = 128L * 1024 * 1024;
    private static readonly string[] RequiredRootFiles =
        ["manifest.yml", "QQListener.dll", "README.md", "CHANGELOG.md", "icon.png"];
    private static readonly Regex CanonicalVersionPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ChecksumMarkerPattern = new(
        @"<!--\s*CLASSISLAND_PKG_MD5\s+(\{.*?\})\s*-->",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    internal static Func<string> TemporaryDirectoryFactory { get; set; } = CreateTemporaryDirectory;

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

        string? projectVersion = null;
        Try(errors, "project", () =>
            projectVersion = ReadProjectVersion(Path.Combine(root, "Directory.Build.props")));

        var cipxPath = Path.Combine(root, "cipx", "QQListener.cipx");
        var checksumsPath = Path.Combine(root, "cipx", "checksums.md");
        PackageMetadata? package = null;
        Try(errors, "archive", () => package = ReadPackage(cipxPath, errors));
        Try(errors, "checksum", () => VerifyChecksum(cipxPath, checksumsPath));

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

        using var archive = ZipFile.OpenRead(cipxPath);
        var auditErrorCount = errors.Count;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var safePaths = new List<(string Path, string SortKey)>();
        var requiredEntries = RequiredRootFiles.ToDictionary(
            name => name,
            _ => (ZipArchiveEntry?)null,
            StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!IsSafeArchivePath(name))
            {
                errors.Add($"archive entry '{name}' is not a safe relative path.");
                continue;
            }

            if (!seen.Add(name))
            {
                errors.Add($"archive entry '{name}' is duplicated by case-insensitive name.");
            }

            safePaths.Add((name, name + "/"));

            var basename = name.Split('/').Last();
            var reservedName = RequiredRootFiles.FirstOrDefault(required =>
                basename.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (reservedName is not null && !name.Equals(reservedName, StringComparison.Ordinal))
            {
                errors.Add($"archive reserved entry '{name}' must use its exact root-level name.");
            }

            if (name.EndsWith(".cipx", StringComparison.OrdinalIgnoreCase)
                || basename.Equals("checksums.md", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".md5", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".md5sum", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"archive entry '{name}' is a nested or generated artifact.");
            }

            if (requiredEntries.ContainsKey(name)) requiredEntries[name] = entry;
        }

        safePaths.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.SortKey, right.SortKey));
        for (var index = 1; index < safePaths.Count; index++)
        {
            var previous = safePaths[index - 1].Path;
            var current = safePaths[index].Path;
            if (current.Length > previous.Length
                && current.StartsWith(previous, StringComparison.OrdinalIgnoreCase)
                && current[previous.Length] == '/')
            {
                errors.Add($"archive entry '{current}' has a file-directory collision.");
            }
        }

        foreach (var required in requiredEntries)
        {
            if (required.Value is null)
            {
                errors.Add($"archive is missing root {required.Key}.");
            }
        }

        if (requiredEntries["manifest.yml"]?.Length > MaxManifestBytes)
        {
            errors.Add("archive manifest.yml is too large.");
        }

        if (requiredEntries["QQListener.dll"]?.Length > MaxDllBytes)
        {
            errors.Add("archive QQListener.dll is too large.");
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
                var entryLimit = entry.FullName switch
                {
                    "manifest.yml" => (long?)MaxManifestBytes,
                    "QQListener.dll" => MaxDllBytes,
                    _ => null
                };
                CopyWithLimit(input, output, entryLimit);
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

    private static void CopyWithLimit(
        Stream input,
        Stream output,
        long? entryLimit)
    {
        var buffer = new byte[81920];
        var entryBytes = 0L;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (entryLimit is not null && bytesRead > entryLimit.Value - entryBytes)
            {
                throw new InvalidDataException("archive entry exceeds the allowed size.");
            }

            output.Write(buffer, 0, bytesRead);
            entryBytes += bytesRead;
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

        var actual = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(cipxPath)));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"checksum mismatch for '{filename}'.");
        }
    }

    private static bool IsSafeArchivePath(string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.Contains('\\')
            || name.Contains('\0')
            || name.Contains(':')
            || Path.IsPathRooted(name))
        {
            return false;
        }

        var segments = name.Split('/');
        return segments.All(segment =>
            !string.IsNullOrEmpty(segment)
            && segment is not "." and not "..");
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
}
