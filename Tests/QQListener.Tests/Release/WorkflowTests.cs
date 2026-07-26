using System.Text.Json;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace QQListener.Tests.Release;

public sealed class WorkflowTests
{
    private const string RestoreRun = "dotnet restore QQListener.sln --locked-mode";
    private const string BuildRun =
        "dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true";
    private const string TestRun =
        "dotnet test QQListener.sln --configuration Release --no-build --no-restore --results-directory Tests/QQListener.Tests/TestResults --logger \"trx;LogFileName=tests.trx\"";
    private const string SamplesRun =
        "dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build --no-restore";
    private const string BranchMetadataRun =
        "dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release --metadata-only";
    private const string TaggedMetadataRun =
        "dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release --metadata-only --tag \"${{ github.ref_name }}\"";
    private const string BuildCipxRun =
        "dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true -p:ContinuousIntegrationBuild=true";
    private const string BranchPackageRun =
        "dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release";
    private const string TaggedPackageRun =
        "dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release --tag \"${{ github.ref_name }}\"";

    [Fact]
    public void BuildWorkflowIsValidAndPreservesReleaseGateOrder()
    {
        var root = FindProjectRoot();
        var path = Path.Combine(root, ".github", "workflows", "build.yml");
        var yaml = File.ReadAllText(path);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        Assert.Single(stream.Documents);
        var steps = ReadBuildSteps(stream.Documents[0]);

        AssertStep(steps, "Restore locked dependencies", condition: null, RestoreRun);
        AssertStep(steps, "Build release", condition: null, BuildRun);
        AssertStep(steps, "Run tests", condition: null, TestRun);
        AssertStep(steps, "Verify toast samples", condition: null, SamplesRun);
        AssertStep(steps, "Verify branch metadata", "github.ref_type != 'tag'", BranchMetadataRun);
        AssertStep(steps, "Verify tagged metadata", "github.ref_type == 'tag'", TaggedMetadataRun);
        AssertStep(steps, "Build CIPX", condition: null, BuildCipxRun);
        AssertStep(steps, "Verify clean install package", "github.ref_type != 'tag'", BranchPackageRun);
        AssertStep(steps, "Verify tagged clean install package", "github.ref_type == 'tag'", TaggedPackageRun);
        AssertUsesStep(steps, "Upload artifacts", "actions/upload-artifact@v4");

        AssertNamedStepsOrdered(
            steps,
            "Restore locked dependencies",
            "Build release",
            "Run tests",
            "Verify toast samples",
            "Verify branch metadata",
            "Build CIPX",
            "Verify clean install package",
            "Upload artifacts");
        AssertNamedStepsOrdered(
            steps,
            "Restore locked dependencies",
            "Build release",
            "Run tests",
            "Verify toast samples",
            "Verify tagged metadata",
            "Build CIPX",
            "Verify tagged clean install package",
            "Upload artifacts");

        Assert.Contains("runs-on: windows-latest", yaml, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", yaml, StringComparison.Ordinal);
        Assert.Contains("cache-dependency-path: '**/packages.lock.json'", yaml, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", yaml, StringComparison.Ordinal);
        Assert.Contains("Tests/QQListener.Tests/TestResults/tests.trx", yaml, StringComparison.Ordinal);
        Assert.Contains("cipx/QQListener.cipx", yaml, StringComparison.Ordinal);
        Assert.Contains("cipx/checksums.md", yaml, StringComparison.Ordinal);

        using var samples = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "Samples", "qq-toast-samples.json")));
        Assert.Equal(23, samples.RootElement.GetArrayLength());
    }

    private static IReadOnlyList<WorkflowStep> ReadBuildSteps(YamlDocument document)
    {
        var root = Assert.IsType<YamlMappingNode>(document.RootNode);
        var jobs = Mapping(root, "jobs");
        var build = Mapping(jobs, "build");
        var sequence = Assert.IsType<YamlSequenceNode>(Value(build, "steps"));
        return sequence.Children
            .Select(node => Assert.IsType<YamlMappingNode>(node))
            .Select(node => new WorkflowStep(
                Scalar(node, "name"),
                OptionalScalar(node, "if"),
                OptionalScalar(node, "run"),
                OptionalScalar(node, "uses")))
            .ToArray();
    }

    private static void AssertStep(
        IReadOnlyList<WorkflowStep> steps,
        string name,
        string? condition,
        string run)
    {
        var step = Assert.Single(steps, candidate => candidate.Name == name);
        Assert.Equal(condition, step.Condition);
        Assert.Equal(run, step.Run);
        Assert.Null(step.Uses);
    }

    private static void AssertUsesStep(
        IReadOnlyList<WorkflowStep> steps,
        string name,
        string uses)
    {
        var step = Assert.Single(steps, candidate => candidate.Name == name);
        Assert.Null(step.Condition);
        Assert.Null(step.Run);
        Assert.Equal(uses, step.Uses);
    }

    private static void AssertNamedStepsOrdered(
        IReadOnlyList<WorkflowStep> steps,
        params string[] names)
    {
        var previous = -1;
        foreach (var name in names)
        {
            var current = steps
                .Select((step, index) => (step, index))
                .Single(item => item.step.Name == name)
                .index;
            Assert.True(current > previous, $"Expected step '{name}' after index {previous}.");
            previous = current;
        }
    }

    private static YamlMappingNode Mapping(YamlMappingNode node, string key) =>
        Assert.IsType<YamlMappingNode>(Value(node, key));

    private static YamlNode Value(YamlMappingNode node, string key)
    {
        Assert.True(node.Children.TryGetValue(new YamlScalarNode(key), out var value));
        return value;
    }

    private static string Scalar(YamlMappingNode node, string key) =>
        Assert.IsType<YamlScalarNode>(Value(node, key)).Value
        ?? throw new InvalidDataException($"Workflow value '{key}' is null.");

    private static string? OptionalScalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? Assert.IsType<YamlScalarNode>(value).Value
            : null;

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

    private sealed record WorkflowStep(
        string Name,
        string? Condition,
        string? Run,
        string? Uses);
}
