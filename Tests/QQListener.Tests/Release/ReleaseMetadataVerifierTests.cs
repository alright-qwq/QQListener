using System.IO.Compression;
using System.Security.Cryptography;
using QQListener.ReleaseVerifier;
using Xunit;

namespace QQListener.Tests.Release;

public sealed class ReleaseMetadataVerifierTests
{
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

    [Theory]
    [InlineData("subdir/manifest.yml")]
    [InlineData("MANIFEST.YML")]
    [InlineData("nested.cipx")]
    [InlineData("checksums.md")]
    [InlineData("../escape.txt")]
    public void UnsafeOrReservedArchiveEntriesAreRejected(string entryName)
    {
        using var fixture = ReleaseFixture.Create(extraEntryName: entryName);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("archive", StringComparison.OrdinalIgnoreCase));
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
    public void CorruptEmbeddedDllCleansTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create(corruptEmbeddedDll: true);
        var tempRoot = fixture.File("temp");
        Directory.CreateDirectory(tempRoot);
        var previousFactory = ReleaseMetadataVerifier.TemporaryDirectoryFactory;
        ReleaseMetadataVerifier.TemporaryDirectoryFactory = () =>
        {
            var path = System.IO.Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        };

        try
        {
            var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

            Assert.False(result.Success);
            Assert.Empty(Directory.GetDirectories(tempRoot));
        }
        finally
        {
            ReleaseMetadataVerifier.TemporaryDirectoryFactory = previousFactory;
        }
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

    private sealed class ReleaseFixture : IDisposable
    {
        private ReleaseFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public string File(string relativePath) =>
            System.IO.Path.Combine(Path, relativePath);

        public static ReleaseFixture Create(
            string? extraEntryName = null,
            string? checksumOverride = null,
            bool corruptEmbeddedDll = false)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"QQListener.ReleaseVerifier.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var fixture = new ReleaseFixture(path);
            fixture.WriteProjectFiles(extraEntryName, checksumOverride, corruptEmbeddedDll);
            return fixture;
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }

        private void WriteProjectFiles(
            string? extraEntryName,
            string? checksumOverride,
            bool corruptEmbeddedDll)
        {
            const string manifest =
                "id: QQListener\nversion: 1.0.3.0\nentranceAssembly: QQListener.dll\n";
            System.IO.File.WriteAllText(File("manifest.yml"), manifest);
            System.IO.File.WriteAllText(
                File("Directory.Build.props"),
                "<Project><PropertyGroup><QQListenerVersion>1.0.3.0</QQListenerVersion></PropertyGroup></Project>");

            var buildDirectory = File(System.IO.Path.Combine(
                "bin",
                "Release",
                "net8.0-windows10.0.19041.0"));
            Directory.CreateDirectory(buildDirectory);
            var buildDll = System.IO.Path.Combine(buildDirectory, "QQListener.dll");
            System.IO.File.Copy(typeof(Plugin).Assembly.Location, buildDll);

            var cipxDirectory = File("cipx");
            Directory.CreateDirectory(cipxDirectory);
            var cipxPath = System.IO.Path.Combine(cipxDirectory, "QQListener.cipx");
            using (var archive = ZipFile.Open(cipxPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.yml", manifest);
                var dllEntry = archive.CreateEntry("QQListener.dll");
                using (var output = dllEntry.Open())
                {
                    if (corruptEmbeddedDll)
                    {
                        output.Write("not-a-dll"u8);
                    }
                    else
                    {
                        using var input = System.IO.File.OpenRead(buildDll);
                        input.CopyTo(output);
                    }
                }

                if (extraEntryName is not null)
                {
                    WriteEntry(archive, extraEntryName, "extra");
                }
            }

            var checksum = checksumOverride
                           ?? Convert.ToHexString(MD5.HashData(System.IO.File.ReadAllBytes(cipxPath)));
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(cipxDirectory, "checksums.md"),
                $"<!-- CLASSISLAND_PKG_MD5 {{\"QQListener.cipx\":\"{checksum}\"}} -->");
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
