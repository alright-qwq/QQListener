using System.Xml.Linq;
using Xunit;

namespace QQListener.Tests.Release;

public sealed class DocumentationTests
{
    [Fact]
    public void UserDocumentationCoversSupportedScopeAndRecovery()
    {
        var root = FindProjectRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));

        Assert.Contains("五步快速开始", readme);
        Assert.Contains("NT QQ", readme);
        Assert.Contains("TIM", readme);
        Assert.Contains("故障排查", readme);
        Assert.Contains("未知应用身份", readme);
        Assert.Contains("QQListener.NormalMessageReceived", readme);
        Assert.Contains("QQListener.ClearLatestMessage", readme);
        Assert.Contains("1.1.0.0", changelog);
    }

    [Fact]
    public void ReleaseMetadataAndOutputUseVersion1100()
    {
        var root = FindProjectRoot();
        var project = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var projectVersion = project
            .Descendants()
            .Single(element => element.Name.LocalName == "QQListenerVersion")
            .Value;
        var manifest = File.ReadAllText(Path.Combine(root, "manifest.yml"));
        var releaseOutput = Path.Combine(
            root,
            "bin",
            "Release",
            "net8.0-windows10.0.19041.0",
            "CHANGELOG.md");

        Assert.Equal("1.1.0.0", projectVersion);
        Assert.Contains("version: 1.1.0.0", manifest);
        Assert.True(File.Exists(releaseOutput), $"Expected release output '{releaseOutput}'.");
    }

    private static string FindProjectRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QQListener.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to find QQListener.sln.");
    }
}
