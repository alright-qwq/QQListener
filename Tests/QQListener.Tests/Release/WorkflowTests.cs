using YamlDotNet.RepresentationModel;
using Xunit;

namespace QQListener.Tests.Release;

public sealed class WorkflowTests
{
    [Fact]
    public void BuildWorkflowIsValidAndPreservesReleaseGateOrder()
    {
        var root = FindProjectRoot();
        var path = Path.Combine(root, ".github", "workflows", "build.yml");
        var yaml = File.ReadAllText(path);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        Assert.Single(stream.Documents);
        Assert.Contains("runs-on: windows-latest", yaml, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", yaml, StringComparison.Ordinal);
        Assert.Contains("cache-dependency-path: '**/packages.lock.json'", yaml, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", yaml, StringComparison.Ordinal);
        Assert.Contains("Tests/QQListener.Tests/TestResults/tests.trx", yaml, StringComparison.Ordinal);

        AssertOrdered(
            yaml,
            "dotnet restore QQListener.sln --locked-mode",
            "dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true",
            "dotnet test QQListener.sln --configuration Release --no-build --no-restore --results-directory Tests/QQListener.Tests/TestResults",
            "dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build --no-restore",
            "--metadata-only",
            "name: Build CIPX",
            "-p:CreateCipx=true",
            "name: Verify branch package",
            "name: Upload artifacts");

        Assert.Contains("if: github.ref_type != 'tag'", yaml, StringComparison.Ordinal);
        Assert.Contains("if: github.ref_type == 'tag'", yaml, StringComparison.Ordinal);
        Assert.Contains("--tag \"${{ github.ref_name }}\"", yaml, StringComparison.Ordinal);
        Assert.Contains("cipx/QQListener.cipx", yaml, StringComparison.Ordinal);
        Assert.Contains("cipx/checksums.md", yaml, StringComparison.Ordinal);
    }

    private static void AssertOrdered(string value, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var current = value.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{fragment}' after index {previous}.");
            previous = current;
        }
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QQListener.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find QQListener.sln.");
    }
}
