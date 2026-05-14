using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class QqListenerSettings : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

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

    [JsonIgnore]
    public IReadOnlyList<string> ImportantPersons => SplitLines(ImportantPersonsText);

    [JsonIgnore]
    public IReadOnlyList<string> ImportantKeywords => SplitLines(ImportantKeywordsText);

    [JsonIgnore]
    public IReadOnlyList<string> Blacklist => SplitLines(BlacklistText);

    [JsonIgnore]
    public string SettingsDirectory { get; set; } = "";

    [JsonIgnore]
    public string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static QqListenerSettings Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        try
        {
            if (!File.Exists(path))
            {
                return new QqListenerSettings { SettingsDirectory = directory };
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<QqListenerSettings>(json) ?? new QqListenerSettings();
            settings.SettingsDirectory = directory;
            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QQListener] 加载设置失败，使用默认设置: {ex.Message}");
            return new QqListenerSettings { SettingsDirectory = directory };
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, JsonOptions));
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
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        return (value ?? "")
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }
}
