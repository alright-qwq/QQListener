using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using QQListener.ReleaseVerifier;
using Xunit;

namespace QQListener.Tests.Release;

public sealed class ReleaseMetadataVerifierTests
{
    private const int MaximumManifestBytes = 1024 * 1024;

    [Fact]
    public void DetectsManifestAndProjectVersionMismatch()
    {
        var result = ReleaseMetadataVerifier.CompareVersions(
            manifestVersion: "1.0.2.0",
            projectVersion: "1.0.3.0",
            assemblyVersion: "1.0.2.0",
            fileVersion: "1.0.2.0",
            tagVersion: null);

        Assert.Contains(result.Errors, error =>
            error.Contains("project", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectsTagMismatch()
    {
        var result = ReleaseMetadataVerifier.CompareVersions(
            "1.0.2.0", "1.0.2.0", "1.0.2.0", "1.0.2.0", "1.0.1.0");

        Assert.Contains(result.Errors, error =>
            error.Contains("tag", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("1.0.2")]
    [InlineData("1.0.2.0.0")]
    [InlineData("v1.0.2.0")]
    [InlineData("1.0.2.0-beta")]
    [InlineData(" 1.0.2.0")]
    [InlineData("1.00.2.0")]
    [InlineData("")]
    public void RejectsNonCanonicalVersions(string value)
    {
        var result = ReleaseMetadataVerifier.CompareVersions(
            value, value, value, value, null);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("canonical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadsStrictClassIslandChecksumMarker()
    {
        const string markdown =
            "<!-- CLASSISLAND_PKG_MD5 {\"QQListener.cipx\":\"0123456789ABCDEF0123456789ABCDEF\"} -->";

        var values = ReleaseMetadataVerifier.ParseChecksums(markdown);

        Assert.Equal(
            "0123456789ABCDEF0123456789ABCDEF",
            values["QQListener.cipx"]);
    }

    [Theory]
    [InlineData("no marker")]
    [InlineData("<!-- CLASSISLAND_PKG_MD5 {} -->")]
    [InlineData("<!-- CLASSISLAND_PKG_MD5 {\"QQListener.cipx\":\"ABC123\"} -->")]
    [InlineData("<!-- CLASSISLAND_PKG_MD5 not-json -->")]
    [InlineData("<!-- CLASSISLAND_PKG_MD5 {\"QQListener.cipx\":\"0123456789ABCDEF0123456789ABCDEF\"} -->\n<!-- CLASSISLAND_PKG_MD5 {\"QQListener.cipx\":\"0123456789ABCDEF0123456789ABCDEF\"} -->")]
    public void RejectsInvalidChecksumMarkers(string markdown)
    {
        Assert.Throws<FormatException>(() =>
            ReleaseMetadataVerifier.ParseChecksums(markdown));
    }

    [Fact]
    public void ValidProjectAndPackagePass()
    {
        using var fixture = ReleaseFixture.Create();

        var result = ReleaseMetadataVerifier.VerifyProject(
            fixture.Path,
            "Release",
            tagVersion: null);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void DuplicateManifestVersionIsRejected()
    {
        using var fixture = ReleaseFixture.Create();
        File.WriteAllText(
            fixture.File("manifest.yml"),
            "id: QQListener\nversion: 1.0.2.0\nversion: 1.0.2.0\nentranceAssembly: QQListener.dll\n");

        var result = ReleaseMetadataVerifier.VerifyMetadata(
            fixture.Path,
            "Release",
            tagVersion: null);

        Assert.Contains(result.Errors, error =>
            error.Contains("manifest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateProjectVersionIsRejected()
    {
        using var fixture = ReleaseFixture.Create();
        File.WriteAllText(
            fixture.File("Directory.Build.props"),
            "<Project><PropertyGroup><QQListenerVersion>1.0.2.0</QQListenerVersion><QQListenerVersion>1.0.2.0</QQListenerVersion></PropertyGroup></Project>");

        var result = ReleaseMetadataVerifier.VerifyMetadata(
            fixture.Path,
            "Release",
            tagVersion: null);

        Assert.Contains(result.Errors, error =>
            error.Contains("project", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageRequiresEveryVersionToBe1100()
    {
        using var fixture = ReleaseFixture.Create(
            version: "1.0.0.0",
            embeddedDllPath: typeof(ReleaseMetadataVerifier).Assembly.Location);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        foreach (var source in new[] { "manifest", "project", "assembly", "file" })
        {
            Assert.Contains(result.Errors, error =>
                error.Contains(source, StringComparison.OrdinalIgnoreCase)
                && error.Contains("required release version", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("manifest.yml")]
    [InlineData("QQListener.dll")]
    [InlineData("README.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData("icon.png")]
    public void PackageRequiresCleanInstallRootFiles(string missing)
    {
        using var fixture = ReleaseFixture.Create();
        fixture.RebuildArchive(excludedEntry: missing);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains(missing, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("nested/../../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/drive.dll")]
    [InlineData("nested//empty.dll")]
    [InlineData("./relative.dll")]
    [InlineData("nested/./relative.dll")]
    [InlineData("nested\\backslash.dll")]
    [InlineData("nested/contains\0nul.dll")]
    public void PackageRejectsNonCanonicalArchivePaths(string entryName)
    {
        using var fixture = ReleaseFixture.Create(
            extraEntries: [new ArchiveItem(entryName, "malicious")]);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("safe relative path", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("subdir/manifest.yml")]
    [InlineData("MANIFEST.YML")]
    [InlineData("subdir/QQListener.dll")]
    [InlineData("qqlistener.DLL")]
    [InlineData("subdir/README.md")]
    [InlineData("readme.MD")]
    [InlineData("subdir/CHANGELOG.md")]
    [InlineData("changelog.MD")]
    [InlineData("subdir/icon.png")]
    [InlineData("ICON.PNG")]
    public void PackageRejectsReservedFilesOutsideTheirExactRootNames(string entryName)
    {
        using var fixture = ReleaseFixture.Create(
            extraEntries: [new ArchiveItem(entryName, "reserved")]);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("exact root-level name", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("nested/package.cipx")]
    [InlineData("nested/checksums.md")]
    [InlineData("nested/package.md5")]
    [InlineData("nested/package.md5sum")]
    public void PackageRejectsNestedOrGeneratedArtifacts(string entryName)
    {
        using var fixture = ReleaseFixture.Create(
            extraEntries: [new ArchiveItem(entryName, "generated")]);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("nested or generated artifact", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageRejectsCaseInsensitiveDuplicatePaths()
    {
        using var fixture = ReleaseFixture.Create(extraEntries:
        [
            new ArchiveItem("assets/value.txt", "first"),
            new ArchiveItem("ASSETS/VALUE.TXT", "second")
        ]);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("case-insensitive", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("assets", "assets/icon.txt")]
    [InlineData("assets/icon.txt", "ASSETS")]
    [InlineData("README.md/child.txt", "other.txt")]
    public void PackageRejectsFileDirectoryCollisions(string first, string second)
    {
        using var fixture = ReleaseFixture.Create(extraEntries:
        [
            new ArchiveItem(first, "first"),
            new ArchiveItem(second, "second")
        ]);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("file-directory collision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanFailureDoesNotCreateTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create(
            extraEntries: [new ArchiveItem("../escape.dll", "malicious")]);
        var factoryCalls = 0;

        WithTemporaryDirectoryFactory(
            () =>
            {
                factoryCalls++;
                throw new InvalidOperationException("factory must not be called");
            },
            () =>
            {
                var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                Assert.False(result.Success);
            });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void ValidPackageCleansTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create();
        var temporaryDirectory = fixture.File("successful-extraction");

        WithTemporaryDirectoryFactory(
            () => CreateDirectory(temporaryDirectory),
            () =>
            {
                var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            });

        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    public void InvalidDllCleansTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create(corruptEmbeddedDll: true);
        var temporaryDirectory = fixture.File("failed-extraction");

        WithTemporaryDirectoryFactory(
            () => CreateDirectory(temporaryDirectory),
            () =>
            {
                var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                Assert.False(result.Success);
            });

        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    public void ExtractionFailureCleansTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create();
        var temporaryDirectory = fixture.File("create-new-collision");

        WithTemporaryDirectoryFactory(
            () =>
            {
                Directory.CreateDirectory(temporaryDirectory);
                File.WriteAllText(Path.Combine(temporaryDirectory, "README.md"), "pre-existing");
                return temporaryDirectory;
            },
            () =>
            {
                var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                Assert.False(result.Success);
            });

        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    public void CleanupFailureIsReported()
    {
        using var fixture = ReleaseFixture.Create();
        var temporaryDirectory = fixture.File("locked-cleanup");
        FileStream? lockStream = null;

        try
        {
            WithTemporaryDirectoryFactory(
                () =>
                {
                    Directory.CreateDirectory(temporaryDirectory);
                    lockStream = new FileStream(
                        Path.Combine(temporaryDirectory, "locked.tmp"),
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return temporaryDirectory;
                },
                () =>
                {
                    var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                    Assert.False(result.Success);
                    Assert.Contains(result.Errors, error =>
                        error.Contains("cleanup", StringComparison.OrdinalIgnoreCase));
                });
        }
        finally
        {
            lockStream?.Dispose();
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ManifestAtSizeLimitIsAccepted()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.RebuildArchive(manifestOverride: ManifestWithExactSize(MaximumManifestBytes));

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void ManifestOverSizeLimitIsRejectedBeforeExtraction()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.RebuildArchive(manifestOverride: ManifestWithExactSize(MaximumManifestBytes + 1));
        AssertScanRejectsBeforeExtraction(fixture);
    }

    [Fact]
    public void PackageAcceptsFiveThousandIndependentEntries()
    {
        var extras = Enumerable.Range(0, 5000)
            .Select(index => new ArchiveItem($"assets/{index:D4}.txt", string.Empty))
            .ToArray();
        using var fixture = ReleaseFixture.Create(extraEntries: extras);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void ArchiveErrorsDoNotExposeManifestContents()
    {
        const string secret = "TOP-SECRET-MANIFEST-CONTENT";
        using var fixture = ReleaseFixture.Create();
        fixture.RebuildArchive(manifestOverride:
            $"id: QQListener\nversion: {secret}\nentranceAssembly: QQListener.dll\n");

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.DoesNotContain(result.Errors, error => error.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void ChecksumMismatchIsRejected()
    {
        using var fixture = ReleaseFixture.Create(checksumOverride: new string('0', 32));

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.Contains(result.Errors, error =>
            error.Contains("checksum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CliReturnsNonZeroForInvalidRoot()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ReleaseVerifierCli.Run(
            ["--root", "missing-root", "--configuration", "Release"],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(error.ToString());
    }

    private static void AssertScanRejectsBeforeExtraction(ReleaseFixture fixture)
    {
        var factoryCalls = 0;
        WithTemporaryDirectoryFactory(
            () =>
            {
                factoryCalls++;
                throw new InvalidOperationException("factory must not be called");
            },
            () =>
            {
                var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
                Assert.False(result.Success);
            });
        Assert.Equal(0, factoryCalls);
    }

    private static string ManifestWithExactSize(int size)
    {
        const string prefix =
            "id: QQListener\nversion: 1.1.0.0\nentranceAssembly: QQListener.dll\n#";
        Assert.True(prefix.Length <= size);
        return prefix + new string('x', size - prefix.Length);
    }

    private static string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WithTemporaryDirectoryFactory(Func<string> factory, Action action)
    {
        var previousFactory = ReleaseMetadataVerifier.TemporaryDirectoryFactory;
        ReleaseMetadataVerifier.TemporaryDirectoryFactory = factory;
        try
        {
            action();
        }
        finally
        {
            ReleaseMetadataVerifier.TemporaryDirectoryFactory = previousFactory;
        }
    }

    private sealed record ArchiveItem(string Name, string Content);

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly string _buildDll;
        private readonly string _manifest;
        private readonly IReadOnlyList<ArchiveItem> _extraEntries;
        private readonly bool _corruptEmbeddedDll;

        private ReleaseFixture(
            string path,
            string buildDll,
            string manifest,
            IReadOnlyList<ArchiveItem> extraEntries,
            bool corruptEmbeddedDll)
        {
            Path = path;
            _buildDll = buildDll;
            _manifest = manifest;
            _extraEntries = extraEntries;
            _corruptEmbeddedDll = corruptEmbeddedDll;
        }

        public string Path { get; }

        private string CipxPath => File(System.IO.Path.Combine("cipx", "QQListener.cipx"));

        public string File(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public static ReleaseFixture Create(
            IReadOnlyList<ArchiveItem>? extraEntries = null,
            string? checksumOverride = null,
            bool corruptEmbeddedDll = false,
            string version = "1.1.0.0",
            string? embeddedDllPath = null)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"QQListener.ReleaseVerifier.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var manifest =
                $"id: QQListener\nversion: {version}\nentranceAssembly: QQListener.dll\n";
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(path, "manifest.yml"),
                manifest);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(path, "Directory.Build.props"),
                $"<Project><PropertyGroup><QQListenerVersion>{version}</QQListenerVersion></PropertyGroup></Project>");

            var buildDirectory = System.IO.Path.Combine(
                path,
                "bin",
                "Release",
                "net8.0-windows10.0.19041.0");
            Directory.CreateDirectory(buildDirectory);
            var buildDll = System.IO.Path.Combine(buildDirectory, "QQListener.dll");
            System.IO.File.Copy(embeddedDllPath ?? typeof(Plugin).Assembly.Location, buildDll);

            Directory.CreateDirectory(System.IO.Path.Combine(path, "cipx"));
            var fixture = new ReleaseFixture(
                path,
                buildDll,
                manifest,
                extraEntries ?? [],
                corruptEmbeddedDll);
            fixture.RebuildArchive(checksumOverride: checksumOverride);
            return fixture;
        }

        public void RebuildArchive(
            string? excludedEntry = null,
            string? manifestOverride = null,
            string? checksumOverride = null)
        {
            if (System.IO.File.Exists(CipxPath))
            {
                System.IO.File.Delete(CipxPath);
            }

            using (var archive = ZipFile.Open(CipxPath, ZipArchiveMode.Create))
            {
                var manifest = manifestOverride ?? _manifest;
                WriteRequiredText(archive, "manifest.yml", manifest, excludedEntry);
                if (!string.Equals(excludedEntry, "QQListener.dll", StringComparison.Ordinal))
                {
                    var dllEntry = archive.CreateEntry("QQListener.dll");
                    using var output = dllEntry.Open();
                    if (_corruptEmbeddedDll)
                    {
                        output.Write("not-a-dll"u8);
                    }
                    else
                    {
                        using var input = System.IO.File.OpenRead(_buildDll);
                        input.CopyTo(output);
                    }
                }

                WriteRequiredText(archive, "README.md", "# QQListener\n", excludedEntry);
                WriteRequiredText(archive, "CHANGELOG.md", "# Changelog\n", excludedEntry);
                WriteRequiredText(archive, "icon.png", "png", excludedEntry);
                foreach (var item in _extraEntries)
                {
                    WriteEntry(archive, item.Name, item.Content);
                }
            }

            WriteChecksum(checksumOverride);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }

        private static void WriteRequiredText(
            ZipArchive archive,
            string name,
            string content,
            string? excludedEntry)
        {
            if (!string.Equals(excludedEntry, name, StringComparison.Ordinal))
            {
                WriteEntry(archive, name, content);
            }
        }

        private void WriteChecksum(string? checksumOverride)
        {
            var checksum = checksumOverride
                           ?? Convert.ToHexString(MD5.HashData(System.IO.File.ReadAllBytes(CipxPath)));
            System.IO.File.WriteAllText(
                File(System.IO.Path.Combine("cipx", "checksums.md")),
                $"<!-- CLASSISLAND_PKG_MD5 {{\"QQListener.cipx\":\"{checksum}\"}} -->");
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }
    }
}
