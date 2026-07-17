# QQListener Release Engineering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make QQListener builds reproducible and make every future branch/tag release prove that manifest, DLL, CIPX, checksum, tests, and documentation agree.

**Architecture:** Pin the .NET toolchain and NuGet graph, declare the plugin version explicitly, add a small testable release verifier, and run the same verifier locally and in Windows CI after Plugin SDK packaging. Preserve the historical `1.0.2.0` tag instead of rewriting published history.

**Tech Stack:** .NET 8.0.422, MSBuild, NuGet lock files, YamlDotNet 16.3.0, System.IO.Compression, GitHub Actions, ClassIsland.PluginSdk CIPX target

---

## File Map

- Create `global.json`: pin the .NET SDK.
- Create `Directory.Build.props`: version and lock-file policy.
- Modify `QQListener.csproj`: project version metadata and test/tool exclusions.
- Modify `manifest.yml`: preserve and commit the existing `1.0.2.0` update.
- Create `Tools/QQListener.ReleaseVerifier/*`: structured manifest/DLL/CIPX/checksum verifier.
- Create `Tests/QQListener.Tests/Release/*`: unit tests for release metadata parsing.
- Create `packages.lock.json` files: locked dependency graphs for every project.
- Create `.github/workflows/build.yml`: Windows build/test/package pipeline.
- Modify `.gitignore`: exclude machine-local settings and Stakpak data.
- Delete tracked `QQListener.sln.DotSettings.user` and `.stakpak/data/local.db`.
- Modify `README.md`: deterministic developer and release instructions.

### Task 1: Pin the SDK, package graph, and plugin version

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Modify: `QQListener.csproj`
- Modify: `manifest.yml`
- Modify: `QQListener.sln`

- [ ] **Step 1: Add the pinned SDK file**

```json
{
  "sdk": {
    "version": "8.0.422",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 2: Add shared deterministic restore properties**

```xml
<Project>
  <PropertyGroup>
    <QQListenerVersion>1.0.2.0</QQListenerVersion>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <RestoreLockedMode Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</RestoreLockedMode>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Bind the plugin assembly metadata to `QQListenerVersion`**

Add to the main project property group:

```xml
<Version>$(QQListenerVersion)</Version>
<AssemblyVersion>$(QQListenerVersion)</AssemblyVersion>
<FileVersion>$(QQListenerVersion)</FileVersion>
<InformationalVersion>$(QQListenerVersion)</InformationalVersion>
```

Keep the user's existing `manifest.yml` value `version: 1.0.2.0`. Do not move the historical Git tag.

- [ ] **Step 4: Exclude `Tools/**` from plugin compilation/resources**

Add the same `Compile`, `AvaloniaResource`, `AvaloniaXaml`, `Content`, `EmbeddedResource`, and `None` removals already used for `Samples/**` and `Tests/**`.

- [ ] **Step 5: Generate and inspect lock files**

Run:

```powershell
dotnet restore QQListener.sln --force-evaluate
dotnet restore QQListener.sln --locked-mode
```

Expected: first command creates one `packages.lock.json` per project; second exits 0 without changing them.

- [ ] **Step 6: Build and inspect DLL versions**

Run:

```powershell
dotnet build QQListener.csproj --configuration Release --no-restore
$dll = Resolve-Path 'bin/Release/net8.0-windows10.0.19041.0/QQListener.dll'
[Reflection.AssemblyName]::GetAssemblyName($dll).Version
[Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
```

Expected: both values are `1.0.2.0`.

- [ ] **Step 7: Commit**

```powershell
git add global.json Directory.Build.props QQListener.csproj QQListener.sln manifest.yml packages.lock.json Samples Tests Tools
git commit -m "build: pin QQListener version and dependencies"
```

### Task 2: Add a testable release verifier

**Files:**
- Create: `Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj`
- Create: `Tools/QQListener.ReleaseVerifier/Program.cs`
- Create: `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs`
- Create: `Tools/QQListener.ReleaseVerifier/VerificationResult.cs`
- Create: `Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs`
- Modify: `Tests/QQListener.Tests/QQListener.Tests.csproj`
- Modify: `QQListener.sln`

- [ ] **Step 1: Create the verifier project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing metadata tests**

```csharp
using QQListener.ReleaseVerifier;

namespace QQListener.Tests.Release;

public class ReleaseMetadataVerifierTests
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

        Assert.Contains(result.Errors, x => x.Contains("project", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectsTagMismatch()
    {
        var result = ReleaseMetadataVerifier.CompareVersions(
            "1.0.2.0", "1.0.2.0", "1.0.2.0", "1.0.2.0", "1.0.1.0");
        Assert.Contains(result.Errors, x => x.Contains("tag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadsClassIslandChecksumMarker()
    {
        const string markdown = "<!-- CLASSISLAND_PKG_MD5 {\"QQListener.cipx\":\"ABC123\"} -->";
        var values = ReleaseMetadataVerifier.ParseChecksums(markdown);
        Assert.Equal("ABC123", values["QQListener.cipx"]);
    }
}
```

- [ ] **Step 3: Run tests and confirm missing verifier types fail**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~ReleaseMetadataVerifierTests`

Expected: FAIL because the verifier project/types do not exist.

- [ ] **Step 4: Add verifier result and comparison logic**

```csharp
namespace QQListener.ReleaseVerifier;

public sealed record VerificationResult(IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

public static partial class ReleaseMetadataVerifier
{
    public static VerificationResult CompareVersions(
        string manifestVersion,
        string projectVersion,
        string assemblyVersion,
        string fileVersion,
        string? tagVersion)
    {
        var errors = new List<string>();
        AddMismatch(errors, "project", manifestVersion, projectVersion);
        AddMismatch(errors, "assembly", manifestVersion, assemblyVersion);
        AddMismatch(errors, "file", manifestVersion, fileVersion);
        if (!string.IsNullOrWhiteSpace(tagVersion))
            AddMismatch(errors, "tag", manifestVersion, tagVersion);
        return new VerificationResult(errors);
    }

    private static void AddMismatch(List<string> errors, string source, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            errors.Add($"{source} version '{actual}' does not match manifest '{expected}'.");
    }
}
```

- [ ] **Step 5: Implement structured manifest and project reads**

Use `YamlDotNet.Serialization.DeserializerBuilder` to deserialize manifest YAML into `Dictionary<string, object>` and `XDocument.Load` to read `QQListenerVersion` from `Directory.Build.props`. Reject missing, blank, or non-four-part versions.

- [ ] **Step 6: Implement DLL and CIPX checks**

Use `AssemblyName.GetAssemblyName` and `FileVersionInfo.GetVersionInfo` for the build DLL. Open CIPX with `ZipFile.OpenRead`, require exactly one `manifest.yml` and `QQListener.dll`, copy the DLL entry to a temporary file, and compare its assembly/file version and embedded manifest version with the project version. Delete the temporary directory in `finally`.

- [ ] **Step 7: Implement checksum verification**

Parse only the `CLASSISLAND_PKG_MD5` HTML comment, deserialize its JSON dictionary with `System.Text.Json`, calculate `MD5.HashData` for the CIPX, and compare case-insensitively with the `QQListener.cipx` entry from `cipx/checksums.md`.

- [ ] **Step 8: Add the CLI**

`Program.cs` accepts `--root <path>`, `--configuration <name>`, and optional `--tag <a.b.c.d>`. It prints each error to stderr and returns 1 on any mismatch; otherwise it prints `Release metadata verified for <version>.` and returns 0.

- [ ] **Step 9: Reference and register projects**

Add a project reference from `QQListener.Tests` to the verifier and add the verifier project to the solution under `Tools`.

- [ ] **Step 10: Run focused tests**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~ReleaseMetadataVerifierTests`

Expected: all three tests pass.

- [ ] **Step 11: Commit**

```powershell
git add Tools/QQListener.ReleaseVerifier Tests/QQListener.Tests QQListener.sln QQListener.csproj
git commit -m "build: verify QQListener release metadata"
```

### Task 3: Verify a real Plugin SDK CIPX package

**Files:**
- Modify: `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs`
- Modify: `Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs`
- Modify: `QQListener.csproj`

- [ ] **Step 1: Write a failing integration test against a supplied package path**

Add a test that creates a temporary ZIP containing a valid YAML manifest and copies the current test assembly as `QQListener.dll`; write a matching `checksums.md`. Assert that changing the manifest version or MD5 produces a failed `VerificationResult` with a specific error.

- [ ] **Step 2: Run the test and confirm package verification is incomplete**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~ReleaseMetadataVerifierTests`

Expected: the new CIPX test fails before full archive/checksum verification is implemented.

- [ ] **Step 3: Complete `VerifyProject`**

```csharp
public static VerificationResult VerifyProject(
    string projectRoot,
    string configuration,
    string? tagVersion)
{
    var manifestPath = Path.Combine(projectRoot, "manifest.yml");
    var propsPath = Path.Combine(projectRoot, "Directory.Build.props");
    var dllPath = Path.Combine(projectRoot, "bin", configuration,
        "net8.0-windows10.0.19041.0", "QQListener.dll");
    var cipxPath = Path.Combine(projectRoot, "cipx", "QQListener.cipx");
    var checksumsPath = Path.Combine(projectRoot, "cipx", "checksums.md");

    return VerifyFiles(manifestPath, propsPath, dllPath, cipxPath, checksumsPath, tagVersion);
}
```

`VerifyFiles` aggregates all errors instead of failing on the first mismatch.

- [ ] **Step 4: Build the package using the SDK target**

Run:

```powershell
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true
```

Expected: `cipx/QQListener.cipx` and `cipx/checksums.md` are recreated by `ClassIsland.PluginSdk.targets`.

- [ ] **Step 5: Run the verifier against the real package**

Run:

```powershell
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release
```

Expected: `Release metadata verified for 1.0.2.0.`

- [ ] **Step 6: Commit**

```powershell
git add Tools/QQListener.ReleaseVerifier Tests/QQListener.Tests QQListener.csproj
git commit -m "test: verify generated ClassIsland package"
```

### Task 4: Add the Windows CI build and package gate

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Add the workflow**

```yaml
name: build

on:
  push:
    branches: [master]
    tags: ['[0-9]*.[0-9]*.[0-9]*.[0-9]*']
  pull_request:
    branches: [master]

permissions:
  contents: read

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: Restore locked dependencies
        run: dotnet restore QQListener.sln --locked-mode
      - name: Build release
        run: dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
      - name: Run tests
        run: dotnet test QQListener.sln --configuration Release --no-build --logger "trx;LogFileName=tests.trx"
      - name: Verify toast samples
        run: dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build
      - name: Build CIPX
        run: dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true -p:ContinuousIntegrationBuild=true
      - name: Verify branch package
        if: github.ref_type != 'tag'
        run: dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release
      - name: Verify tagged package
        if: github.ref_type == 'tag'
        run: dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release --tag "${{ github.ref_name }}"
      - uses: actions/upload-artifact@v4
        with:
          name: QQListener-${{ github.sha }}
          path: |
            cipx/QQListener.cipx
            cipx/checksums.md
            Tests/QQListener.Tests/TestResults/tests.trx
```

- [ ] **Step 2: Validate YAML and commands locally**

Run each `dotnet` command from the workflow in the same order. Parse the workflow with a YAML parser available through the release verifier test to ensure it is valid YAML.

Expected: all commands exit 0 and the workflow deserializes without errors.

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/build.yml
git commit -m "ci: build test and package QQListener"
```

### Task 5: Remove machine-local tracked files and document release usage

**Files:**
- Modify: `.gitignore`
- Delete: `QQListener.sln.DotSettings.user`
- Delete: `.stakpak/data/local.db`
- Modify: `README.md`

- [ ] **Step 1: Add repository hygiene rules**

```gitignore
*.DotSettings.user
.stakpak/data/
```

Keep existing `cipx/`, `*.md5sum`, and local reference exclusions.

- [ ] **Step 2: Remove tracked local artifacts**

Run:

```powershell
git rm -- QQListener.sln.DotSettings.user .stakpak/data/local.db
```

Delete ignored root-level `QQListener.cipx.md5sum`, `checksums.md.md5sum`, and the copied `generate-md5.ps1`; the SDK package owns checksum generation.

- [ ] **Step 3: Add README developer commands**

Document these exact supported commands:

```powershell
dotnet restore QQListener.sln --locked-mode
dotnet build QQListener.sln --configuration Release --no-restore
dotnet test QQListener.sln --configuration Release --no-build
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release
```

Also document that existing tag `1.0.2.0` points at a historical commit whose manifest was `1.0.1.0`; it must not be force-moved. A future release must increment the manifest/`QQListenerVersion` before creating a new four-part tag.

- [ ] **Step 4: Confirm ignored artifacts are no longer tracked**

Run:

```powershell
git ls-files | rg "DotSettings.user|\.stakpak/data|\.md5sum$|generate-md5.ps1"
```

Expected: no output.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore README.md
git commit -m "chore: remove local artifacts from QQListener"
```

### Task 6: Execute the complete release gate

**Files:**
- Modify: release files only if verification exposes a concrete defect

- [ ] **Step 1: Prove locked restore**

Run: `dotnet restore QQListener.sln --locked-mode`

Expected: exit 0 and no lock-file changes.

- [ ] **Step 2: Prove the Release build and tests**

Run:

```powershell
dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test QQListener.sln --configuration Release --no-build
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build
```

Expected: build exit 0, all xUnit tests pass, and 23 samples verify.

- [ ] **Step 3: Recreate and verify CIPX**

Run:

```powershell
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true -p:ContinuousIntegrationBuild=true
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release
```

Expected: package build exits 0 and verifier prints `Release metadata verified for 1.0.2.0.`

- [ ] **Step 4: Confirm only intentional changes remain**

Run:

```powershell
git status --short
git diff --check
```

Expected: no generated `bin`, `obj`, `cipx`, local database, IDE file, or root checksum appears.

- [ ] **Step 5: Commit any verification-only correction, otherwise leave the tree unchanged**

When a correction was required, commit only the files needed for that verified defect with message `fix: complete QQListener release gate`. Do not amend or move the historical tag.
