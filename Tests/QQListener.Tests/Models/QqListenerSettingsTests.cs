using System.Text.Json;
using QQListener.Models;
using QQListener.Views.SettingsPages;
using Xunit;

namespace QQListener.Tests.Models;

public sealed class QqListenerSettingsTests
{
    [Fact]
    public void MissingPrimaryAndBackupUsesDisabledDefaults()
    {
        using var directory = new TemporaryDirectory();

        var settings = QqListenerSettings.Load(directory.Path);

        Assert.False(settings.IsEnabled);
        Assert.True(settings.HasLoadWarning);
        Assert.NotEmpty(settings.LoadWarning);
        Assert.False(File.Exists(settings.SettingsFilePath));
    }

    [Fact]
    public void MissingPrimaryLoadsValidBackupDisabledWithWarning()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            directory.File("settings.json.bak"),
            "{\"IsEnabled\":true,\"BlacklistText\":\"secret\"}");

        var settings = QqListenerSettings.Load(directory.Path);

        Assert.False(settings.IsEnabled);
        Assert.Equal("secret", settings.BlacklistText);
        Assert.True(settings.HasLoadWarning);
        Assert.False(File.Exists(settings.SettingsFilePath));
    }

    [Fact]
    public void CorruptPrimaryLoadsBackupDisabledWithoutOverwritingPrimary()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.File("settings.json"), "{broken");
        File.WriteAllText(
            directory.File("settings.json.bak"),
            "{\"IsEnabled\":true,\"BlacklistText\":\"secret\"}");

        var settings = QqListenerSettings.Load(directory.Path);

        Assert.False(settings.IsEnabled);
        Assert.Equal("secret", settings.BlacklistText);
        Assert.True(settings.HasLoadWarning);
        Assert.DoesNotContain("secret", settings.LoadWarning);
        Assert.Equal("{broken", File.ReadAllText(directory.File("settings.json")));
    }

    [Fact]
    public void CorruptPrimaryAndBackupUseDisabledDefaultsWithoutOverwritingFiles()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.File("settings.json"), "bad-primary");
        File.WriteAllText(directory.File("settings.json.bak"), "bad-backup");

        var settings = QqListenerSettings.Load(directory.Path);

        Assert.False(settings.IsEnabled);
        Assert.True(settings.HasLoadWarning);
        Assert.Equal("bad-primary", File.ReadAllText(directory.File("settings.json")));
        Assert.Equal("bad-backup", File.ReadAllText(directory.File("settings.json.bak")));
    }

    [Fact]
    public void SaveCreatesBackupAndLeavesValidJson()
    {
        using var directory = new TemporaryDirectory();
        var settings = new QqListenerSettings
        {
            SettingsDirectory = directory.Path,
            IsEnabled = false,
            BlacklistText = "first"
        };
        settings.Save();
        settings.BlacklistText = "second";

        settings.Save();

        Assert.Equal("second", ReadString(settings.SettingsFilePath, "BlacklistText"));
        Assert.Equal("first", ReadString(settings.BackupFilePath, "BlacklistText"));
    }

    [Fact]
    public void ModifyAndSaveCreatesBackupAndLeavesValidJson()
    {
        using var directory = new TemporaryDirectory();
        var settings = new QqListenerSettings
        {
            SettingsDirectory = directory.Path,
            IsEnabled = false,
            BlacklistText = "first"
        };
        settings.Save();

        settings.ModifyAndSave(value => value.BlacklistText = "second");

        Assert.Equal("second", ReadString(settings.SettingsFilePath, "BlacklistText"));
        Assert.Equal("first", ReadString(settings.BackupFilePath, "BlacklistText"));
    }

    [Fact]
    public void RecoveredBackupFirstSaveKeepsPrimaryAndBackupValid()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.File("settings.json"), "{broken");
        File.WriteAllText(
            directory.File("settings.json.bak"),
            "{\"IsEnabled\":true,\"BlacklistText\":\"backup\"}");
        var settings = QqListenerSettings.Load(directory.Path);
        settings.BlacklistText = "recovered";

        settings.Save();

        Assert.Equal("recovered", ReadString(settings.SettingsFilePath, "BlacklistText"));
        Assert.Equal("backup", ReadString(settings.BackupFilePath, "BlacklistText"));
    }

    [Fact]
    public void FailedReplacePreservesFilesAndDeletesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var settings = new QqListenerSettings
        {
            SettingsDirectory = directory.Path,
            IsEnabled = false,
            BlacklistText = "first"
        };
        settings.Save();
        settings.BlacklistText = "second";
        settings.Save();
        var primaryBefore = File.ReadAllText(settings.SettingsFilePath);
        var backupBefore = File.ReadAllText(settings.BackupFilePath);
        settings.BlacklistText = "third";

        using var primaryLock = new FileStream(
            settings.SettingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Assert.ThrowsAny<IOException>(() => settings.Save());
        Assert.Equal(primaryBefore, File.ReadAllText(settings.SettingsFilePath));
        Assert.Equal(backupBefore, File.ReadAllText(settings.BackupFilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.*.tmp"));
    }

    [Fact]
    public void PersistenceCoordinatorFlushesOnDetachAndSavesAfterReattach()
    {
        using var directory = new TemporaryDirectory();
        var settings = new QqListenerSettings
        {
            SettingsDirectory = directory.Path,
            IsEnabled = false
        };
        var diagnostics = new QqListenerDiagnosticsState();
        var coordinator = new QqListenerSettingsPersistenceCoordinator(
            settings,
            diagnostics,
            _ => { });

        coordinator.Attach();
        settings.BlacklistText = "first";
        coordinator.Detach();
        Assert.Equal("first", ReadString(settings.SettingsFilePath, "BlacklistText"));

        coordinator.Attach();
        settings.BlacklistText = "second";
        coordinator.Detach();
        Assert.Equal("second", ReadString(settings.SettingsFilePath, "BlacklistText"));
    }

    [Fact]
    public void PersistenceCoordinatorKeepsDirtyStateAfterSaveFailureForRetry()
    {
        using var directory = new TemporaryDirectory();
        var invalidDirectory = directory.File("not-a-directory");
        File.WriteAllText(invalidDirectory, "blocking file");
        var settings = new QqListenerSettings
        {
            SettingsDirectory = invalidDirectory,
            IsEnabled = false
        };
        var diagnostics = new QqListenerDiagnosticsState();
        var coordinator = new QqListenerSettingsPersistenceCoordinator(
            settings,
            diagnostics,
            _ => { });

        coordinator.Attach();
        settings.BlacklistText = "retry";
        coordinator.Detach();

        Assert.True(coordinator.IsDirty);
        Assert.Contains("保存设置失败", diagnostics.LastErrorSummary);

        settings.SettingsDirectory = directory.Path;
        coordinator.Attach();
        coordinator.Detach();

        Assert.False(coordinator.IsDirty);
        Assert.Equal("retry", ReadString(settings.SettingsFilePath, "BlacklistText"));
    }

    private static string ReadString(string path, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(propertyName).GetString() ?? "";
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"QQListener.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
