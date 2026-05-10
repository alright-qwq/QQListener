using System.Text.Json;
using System.Text.Json.Serialization;

namespace QQListener.Models;

public class QqListenerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool IsEnabled { get; set; } = true;

    public bool QqOnly { get; set; } = true;

    public bool SomeoneAtMe { get; set; } = true;

    public bool CallingEnabled { get; set; } = true;

    public string CallingKeyword { get; set; } = "呼叫";

    public double ScanIntervalSeconds { get; set; } = 0.3;

    public int CooldownSeconds { get; set; } = 3;

    public int NormalDurationSeconds { get; set; } = 5;

    public int ImportantDurationSeconds { get; set; } = 10;

    public int CallingDurationSeconds { get; set; } = 600;

    public double RollingSpeed { get; set; } = 1.0;

    public string ImportantPersonsText { get; set; } = "";

    public string ImportantKeywordsText { get; set; } = "作业\n通知\n考试";

    public string BlacklistText { get; set; } = "";

    [JsonIgnore]
    public IReadOnlyList<string> ImportantPersons => SplitLines(ImportantPersonsText);

    [JsonIgnore]
    public IReadOnlyList<string> ImportantKeywords => SplitLines(ImportantKeywordsText);

    [JsonIgnore]
    public IReadOnlyList<string> Blacklist => SplitLines(BlacklistText);

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassIsland",
        "Plugins",
        "QQListener",
        "settings.json");

    public static QqListenerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new QqListenerSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<QqListenerSettings>(json) ?? new QqListenerSettings();
        }
        catch
        {
            return new QqListenerSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void Normalize()
    {
        ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 0.1, 5);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 1, 3600);
        NormalDurationSeconds = Math.Clamp(NormalDurationSeconds, 1, 3600);
        ImportantDurationSeconds = Math.Clamp(ImportantDurationSeconds, 1, 3600);
        CallingDurationSeconds = Math.Clamp(CallingDurationSeconds, 1, 86400);
        // Allow 0 to represent "no scrolling"
        RollingSpeed = Math.Clamp(RollingSpeed, 0.0, 4.0);
        CallingKeyword = CallingKeyword.Trim();
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        return value
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }
}
