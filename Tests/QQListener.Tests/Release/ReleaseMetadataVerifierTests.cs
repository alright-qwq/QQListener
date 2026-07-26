using System.Buffers.Binary;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageRejectsLeafCollisionSeparatedByOrdinalNeighbor(bool reverseOrder)
    {
        var entries = new[]
        {
            new ArchiveItem("a", "leaf"),
            new ArchiveItem("a-", "ordinal neighbor"),
            new ArchiveItem("a/b", "child")
        };
        if (reverseOrder)
        {
            Array.Reverse(entries);
        }

        using var fixture = ReleaseFixture.Create(extraEntries: entries);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("file-directory collision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageAuditsNearZipLimitDeepPathWithoutCreatingTemporaryDirectory()
    {
        var deepPrefix = "leaf/" + string.Join('/', Enumerable.Repeat("a", 4096)) + "/";
        var deepPath = deepPrefix + new string('z', 65000 - deepPrefix.Length);
        Assert.Equal(65000, Encoding.UTF8.GetByteCount(deepPath));
        using var fixture = ReleaseFixture.Create(extraEntries:
        [
            new ArchiveItem("leaf", "file"),
            new ArchiveItem(deepPath, "child")
        ]);

        AssertScanRejectsBeforeExtraction(fixture, "file-directory collision");
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
        var protectedParentDirectory = fixture.File("locked-cleanup-parent");
        var temporaryDirectory = Path.Combine(protectedParentDirectory, "extraction");
        FileStream? lockStream = null;
        UnixFileMode? originalParentMode = null;

        try
        {
            WithTemporaryDirectoryFactory(
                () =>
                {
                    Directory.CreateDirectory(temporaryDirectory);
                    if (OperatingSystem.IsWindows())
                    {
                        lockStream = new FileStream(
                            Path.Combine(temporaryDirectory, "locked.tmp"),
                            FileMode.CreateNew,
                            FileAccess.ReadWrite,
                            FileShare.None);
                    }
                    else
                    {
                        originalParentMode = File.GetUnixFileMode(protectedParentDirectory);
                        File.SetUnixFileMode(
                            protectedParentDirectory,
                            originalParentMode.Value
                            & ~(UnixFileMode.UserWrite
                                | UnixFileMode.GroupWrite
                                | UnixFileMode.OtherWrite));
                    }

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
            if (!OperatingSystem.IsWindows()
                && originalParentMode is not null
                && Directory.Exists(protectedParentDirectory))
            {
                File.SetUnixFileMode(protectedParentDirectory, originalParentMode.Value);
            }

            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            if (Directory.Exists(protectedParentDirectory))
            {
                Directory.Delete(protectedParentDirectory);
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
    public void PackageAcceptsMaximumEntryCount()
    {
        var extras = Enumerable.Range(0, 507)
            .Select(index => new ArchiveItem($"assets/{index:D4}.txt", string.Empty))
            .ToArray();
        using var fixture = ReleaseFixture.Create(extraEntries: extras);

        var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void PackageRejectsEntryCountOverLimitBeforeExtraction()
    {
        var extras = Enumerable.Range(0, 508)
            .Select(index => new ArchiveItem($"assets/{index:D4}.txt", string.Empty))
            .ToArray();
        using var fixture = ReleaseFixture.Create(extraEntries: extras);

        AssertScanRejectsBeforeExtraction(fixture, "more than 512 entries");
    }

    [Fact]
    public void ClassicEocdRejectsDeclaredEntryCountBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateClassicEocd(entryCount: 513));

        AssertScanRejectsBeforeExtraction(
            fixture,
            "archive metadata declares more than 512 entries");
    }

    [Fact]
    public void Zip64EocdRejectsDeclaredEntryCountBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateZip64Eocd(entryCount: 513));

        AssertScanRejectsBeforeExtraction(
            fixture,
            "archive metadata declares more than 512 entries");
    }

    [Fact]
    public void OversizedCentralDirectoryMetadataIsRejectedBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateClassicEocd(
            entryCount: 1,
            centralDirectorySize: 32U * 1024 * 1024 + 1));

        AssertScanRejectsBeforeExtraction(
            fixture,
            "central directory exceeds the 32 MiB safety limit");
    }

    [Fact]
    public void OverlappingClassicCentralDirectoryIsRejectedBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateClassicEocd(
            entryCount: 1,
            centralDirectorySize: 11,
            prefixLength: 10));

        AssertScanRejectsBeforeExtraction(fixture, "central directory overlaps ZIP metadata");
    }

    [Fact]
    public void TruncatedZip64LocatorIsRejectedBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateClassicEocd(
            entryCount: ushort.MaxValue,
            centralDirectorySize: uint.MaxValue,
            centralDirectoryOffset: uint.MaxValue));

        AssertScanRejectsBeforeExtraction(fixture, "ZIP64 locator is missing or truncated");
    }

    [Fact]
    public void OutOfRangeZip64RecordOffsetIsRejectedBeforeMaterialization()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive(CreateZip64LocatorWithOffset(ulong.MaxValue));

        AssertScanRejectsBeforeExtraction(fixture, "ZIP64 record offset is out of range");
    }

    [Fact]
    public void ChecksumMismatchPreventsZipMetadataParsing()
    {
        using var fixture = ReleaseFixture.Create();
        fixture.ReplaceArchive([0x01, 0x02, 0x03], checksumOverride: new string('0', 32));
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
                Assert.Contains(result.Errors, error =>
                    error.Contains("checksum", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(result.Errors, error =>
                    error.Contains("ZIP metadata", StringComparison.OrdinalIgnoreCase));
            });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void DefaultArchiveBudgetAcceptsExactEntryAndTotalLimits()
    {
        var budget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();

        Assert.Null(budget.AuditDeclared("first.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
        Assert.Null(budget.AuditDeclared("second.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
    }

    [Fact]
    public void DefaultArchiveBudgetRejectsEntryAndTotalOverflow()
    {
        var entryBudget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();
        Assert.Contains("oversized.bin", entryBudget.AuditDeclared(
            "oversized.bin", 128L * 1024 * 1024 + 1, 128L * 1024 * 1024));

        var totalBudget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();
        Assert.Null(totalBudget.AuditDeclared("first.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
        Assert.Null(totalBudget.AuditDeclared("second.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
        Assert.Contains("third.bin", totalBudget.AuditDeclared("third.bin", 1, 128L * 1024 * 1024));
    }

    [Fact]
    public void CopyEntryEnforcesActualEntryAndTotalBytes()
    {
        var entryBudget = new ArchiveResourceBudget(totalLimit: 10);
        Assert.Throws<InvalidDataException>(() => entryBudget.CopyEntry(
            new MemoryStream(new byte[6]), Stream.Null, "entry.bin", entryLimit: 5));

        var totalBudget = new ArchiveResourceBudget(totalLimit: 10);
        totalBudget.CopyEntry(new MemoryStream(new byte[6]), Stream.Null, "first.bin", entryLimit: 10);
        var exception = Assert.Throws<InvalidDataException>(() => totalBudget.CopyEntry(
            new MemoryStream(new byte[5]), Stream.Null, "second.bin", entryLimit: 10));
        Assert.Contains("second.bin", exception.Message, StringComparison.Ordinal);
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
    public void OversizedCipxIsRejectedBeforeChecksumOrExtraction()
    {
        using var fixture = ReleaseFixture.Create();
        using (var stream = new FileStream(fixture.CipxFile, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(256L * 1024 * 1024 + 1);
        }

        AssertScanRejectsBeforeExtraction(fixture, "CIPX file is too large");
    }

    [Fact]
    public void ChecksumMismatchDoesNotCreateTemporaryDirectory()
    {
        using var fixture = ReleaseFixture.Create(checksumOverride: new string('0', 32));

        AssertScanRejectsBeforeExtraction(fixture, "checksum");
    }

    [Fact]
    public void OversizedChecksumsFileIsRejectedBeforeExtraction()
    {
        using var fixture = ReleaseFixture.Create();
        using (var stream = new FileStream(
                   fixture.ChecksumsFile, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(1024L * 1024 + 1);
        }

        AssertScanRejectsBeforeExtraction(fixture, "checksums.md is too large");
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

    private static void AssertScanRejectsBeforeExtraction(
        ReleaseFixture fixture,
        string? expectedError = null)
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
                if (expectedError is not null)
                {
                    Assert.Contains(result.Errors, error =>
                        error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
                }
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

    private static byte[] CreateClassicEocd(
        ushort entryCount,
        uint centralDirectorySize = 0,
        uint centralDirectoryOffset = 0,
        int prefixLength = 0)
    {
        var bytes = new byte[prefixLength + 22];
        var eocd = bytes.AsSpan(prefixLength);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[8..], entryCount);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[10..], entryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[12..], centralDirectorySize);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[16..], centralDirectoryOffset);
        return bytes;
    }

    private static byte[] CreateZip64Eocd(ulong entryCount)
    {
        var bytes = new byte[56 + 20 + 22];
        var record = bytes.AsSpan(0, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(record[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(record[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(record[14..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(record[24..], entryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(record[32..], entryCount);

        var locator = bytes.AsSpan(56, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);

        WriteZip64ClassicEocd(bytes.AsSpan(76, 22));
        return bytes;
    }

    private static byte[] CreateZip64LocatorWithOffset(ulong recordOffset)
    {
        var bytes = new byte[20 + 22];
        var locator = bytes.AsSpan(0, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], recordOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
        WriteZip64ClassicEocd(bytes.AsSpan(20, 22));
        return bytes;
    }

    private static void WriteZip64ClassicEocd(Span<byte> eocd)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[8..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[10..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[12..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[16..], uint.MaxValue);
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

        public string CipxFile => CipxPath;

        public string ChecksumsFile => File(System.IO.Path.Combine("cipx", "checksums.md"));

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

        public void ReplaceArchive(byte[] contents, string? checksumOverride = null)
        {
            System.IO.File.WriteAllBytes(CipxPath, contents);
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
            string checksum;
            if (checksumOverride is not null)
            {
                checksum = checksumOverride;
            }
            else
            {
                using var stream = System.IO.File.OpenRead(CipxPath);
                checksum = Convert.ToHexString(MD5.HashData(stream));
            }

            System.IO.File.WriteAllText(
                ChecksumsFile,
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
