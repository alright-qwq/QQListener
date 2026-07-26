using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class QqListenerSettings : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _saveLock = new();
    private readonly Action<string, string, string?, bool> _replaceFile;
    private volatile int _suppressAutoSaveCount;
    private bool _preserveBackupOnNextSave;

    public QqListenerSettings() : this(File.Replace)
    {
    }

    internal QqListenerSettings(Action<string, string, string?, bool> replaceFile)
    {
        ArgumentNullException.ThrowIfNull(replaceFile);
        _replaceFile = replaceFile;
    }

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _qqOnly = true;

    [ObservableProperty]
    private bool _someoneAtMe = true;

    [ObservableProperty]
    private bool _callingEnabled = true;

    [ObservableProperty]
    private string _callingKeyword = "呼叫";

    [ObservableProperty]
    private double _scanIntervalSeconds = 0.3;

    [ObservableProperty]
    private int _cooldownSeconds = 3;

    [ObservableProperty]
    private int _normalDurationSeconds = 5;

    [ObservableProperty]
    private int _importantDurationSeconds = 10;

    [ObservableProperty]
    private int _callingDurationSeconds = 600;

    [ObservableProperty]
    private double _rollingSpeed = 1.0;

    [ObservableProperty]
    private string _importantPersonsText = "";

    [ObservableProperty]
    private string _importantKeywordsText = "作业\n通知\n考试";

    [ObservableProperty]
    private string _blacklistText = "";

    [ObservableProperty]
    private string _allowedSourcesText = "";

    [ObservableProperty]
    private bool _ruleTimeWindowEnabled;

    [ObservableProperty]
    private string _ruleTimeWindowStartText = "00:00";

    [ObservableProperty]
    private string _ruleTimeWindowEndText = "00:00";

    [ObservableProperty]
    private bool _normalTopmost;

    [ObservableProperty]
    private bool _importantTopmost;

    [ObservableProperty]
    private bool _callingTopmost = true;

    [JsonIgnore]
    public IReadOnlyList<string> ImportantPersons => SplitLines(ImportantPersonsText);

    [JsonIgnore]
    public IReadOnlyList<string> ImportantKeywords => SplitLines(ImportantKeywordsText);

    [JsonIgnore]
    public IReadOnlyList<string> Blacklist => SplitLines(BlacklistText);

    [JsonIgnore]
    public IReadOnlyList<string> AllowedSources => SplitLines(AllowedSourcesText);

    [JsonIgnore]
    public bool SuppressAutoSave => _suppressAutoSaveCount > 0;

    [JsonIgnore]
    public string SettingsDirectory { get; set; } = "";

    [JsonIgnore]
    public string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    [JsonIgnore]
    public string BackupFilePath => SettingsFilePath + ".bak";

    [JsonIgnore]
    public bool HasLoadWarning { get; private set; }

    [JsonIgnore]
    public string LoadWarning { get; private set; } = "";

    public static QqListenerSettings Load(string directory)
    {
        var primaryPath = Path.Combine(directory, "settings.json");
        var backupPath = primaryPath + ".bak";

        if (TryLoadFile(primaryPath, directory, out var primary))
        {
            return primary;
        }

        if (TryLoadFile(backupPath, directory, out var backup))
        {
            backup.IsEnabled = false;
            backup.HasLoadWarning = true;
            backup.LoadWarning = File.Exists(primaryPath)
                ? "主设置文件无法读取，已从备份恢复并关闭监听。"
                : "主设置文件缺失，已从备份恢复并关闭监听。";
            backup._preserveBackupOnNextSave = true;
            return backup;
        }

        return new QqListenerSettings
        {
            SettingsDirectory = directory,
            IsEnabled = false,
            HasLoadWarning = true,
            LoadWarning = File.Exists(primaryPath) || File.Exists(backupPath)
                ? "设置文件及备份均无法读取，已使用关闭监听的默认设置。"
                : "未找到设置文件或备份，监听保持关闭。"
        };
    }

    public void Save()
    {
        lock (_saveLock)
        {
            SaveCoreAtomic();
        }
    }

    /// <summary>
    /// 原子地修改设置并保存。在修改期间抑制自动保存。
    /// </summary>
    public void ModifyAndSave(Action<QqListenerSettings> modifier)
    {
        lock (_saveLock)
        {
            Interlocked.Increment(ref _suppressAutoSaveCount);
            try
            {
                modifier(this);
            }
            finally
            {
                Interlocked.Decrement(ref _suppressAutoSaveCount);
            }
            SaveCoreAtomic();
        }
    }

    public void Normalize()
    {
        ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 0.1, 5);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 1, 3600);
        NormalDurationSeconds = Math.Clamp(NormalDurationSeconds, 1, 3600);
        ImportantDurationSeconds = Math.Clamp(ImportantDurationSeconds, 1, 3600);
        CallingDurationSeconds = Math.Clamp(CallingDurationSeconds, 1, 86400);
        RollingSpeed = Math.Clamp(RollingSpeed, 0.0, 4.0);
        CallingKeyword = (CallingKeyword ?? "").Trim();

        if (!TimeOnly.TryParseExact(
                RuleTimeWindowStartText,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var start)
            || !TimeOnly.TryParseExact(
                RuleTimeWindowEndText,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var end))
        {
            RuleTimeWindowStartText = "00:00";
            RuleTimeWindowEndText = "00:00";
            return;
        }

        RuleTimeWindowStartText = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        RuleTimeWindowEndText = end.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        return (value ?? "")
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private void SaveCoreAtomic()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(SettingsFilePath))
            {
                File.Move(temporaryPath, SettingsFilePath);
            }
            else if (_preserveBackupOnNextSave)
            {
                _replaceFile(temporaryPath, SettingsFilePath, null, true);
            }
            else
            {
                _replaceFile(
                    temporaryPath,
                    SettingsFilePath,
                    BackupFilePath,
                    true);
            }

            _preserveBackupOnNextSave = false;
            HasLoadWarning = false;
            LoadWarning = "";
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryLoadFile(
        string path,
        string directory,
        out QqListenerSettings settings)
    {
        settings = null!;
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            settings = JsonSerializer.Deserialize<QqListenerSettings>(json)
                       ?? throw new JsonException("设置内容为空。");
            settings.SettingsDirectory = directory;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
