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
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxDllBytes = 128L * 1024 * 1024;
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
                errors.Add($"build DLL assembly name '{dll.SimpleName}' is not 'QQListener'.");
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
            if (!string.Equals(package.Manifest.EntranceAssembly, "QQListener.dll", StringComparison.Ordinal))
            {
                errors.Add("archive manifest entranceAssembly must be exactly 'QQListener.dll'.");
            }

            if (!string.Equals(package.Dll.SimpleName, "QQListener", StringComparison.Ordinal))
            {
                errors.Add($"archive DLL assembly name '{package.Dll.SimpleName}' is not 'QQListener'.");
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ZipArchiveEntry? manifestEntry = null;
        ZipArchiveEntry? dllEntry = null;

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

            var basename = name.Split('/').Last();
            if ((basename.Equals("manifest.yml", StringComparison.OrdinalIgnoreCase)
                 && !name.Equals("manifest.yml", StringComparison.Ordinal))
                || (basename.Equals("QQListener.dll", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("QQListener.dll", StringComparison.Ordinal)))
            {
                errors.Add($"archive reserved entry '{name}' must use its exact root-level name.");
            }

            if (name.EndsWith(".cipx", StringComparison.OrdinalIgnoreCase)
                || basename.Equals("checksums.md", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".md5sum", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"archive entry '{name}' is a nested or generated artifact.");
            }

            if (name.Equals("manifest.yml", StringComparison.Ordinal)) manifestEntry = entry;
            if (name.Equals("QQListener.dll", StringComparison.Ordinal)) dllEntry = entry;
        }

        if (manifestEntry is null) throw new FormatException("archive is missing root manifest.yml.");
        if (dllEntry is null) throw new FormatException("archive is missing root QQListener.dll.");
        if (manifestEntry.Length > MaxManifestBytes) throw new FormatException("archive manifest.yml is too large.");
        if (dllEntry.Length > MaxDllBytes) throw new FormatException("archive QQListener.dll is too large.");

        ManifestMetadata manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            manifest = ParseManifest(reader.ReadToEnd());
        }

        string? tempDirectory = null;
        try
        {
            tempDirectory = TemporaryDirectoryFactory();
            var dllPath = Path.Combine(tempDirectory, "QQListener.dll");
            using (var input = dllEntry.Open())
            using (var output = new FileStream(dllPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
            }

            return new PackageMetadata(manifest, ReadDll(dllPath));
        }
        finally
        {
            if (tempDirectory is not null && Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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
                $"{source} version '{value}' is not a canonical four-part version.");
        }

        var parts = value.Split('.');
        if (parts.Any(part => !int.TryParse(part, out _))
            || new System.Version(value).ToString(4) != value)
        {
            throw new FormatException(
                $"{source} version '{value}' is not a canonical four-part version.");
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
            errors.Add($"{source} version '{actual}' does not match manifest '{expected}'.");
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
