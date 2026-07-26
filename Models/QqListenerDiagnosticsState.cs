using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class QqListenerDiagnosticsState : ObservableObject
{
    [ObservableProperty]
    private string _runtimeSummary = "尚未初始化";

    [ObservableProperty]
    private string _assemblyPath = "未知";

    [ObservableProperty]
    private string _pluginFolderPath = "未知";

    [ObservableProperty]
    private string _settingsPath = "未知";

    [ObservableProperty]
    private string _listenerState = "未启动";

    [ObservableProperty]
    private string _permissionStatus = "未请求";

    [ObservableProperty]
    private string _appIdentityStatus = "尚未读取";

    [ObservableProperty]
    private string _lastReadSummary = "尚未读取";

    [ObservableProperty]
    private string _lastCandidateSummary = "尚未筛选";

    [ObservableProperty]
    private string _lastAcceptedSummary = "暂无";

    [ObservableProperty]
    private string _lastIgnoredReason = "暂无";

    [ObservableProperty]
    private string _lastFormatSummary = "暂无";

    [ObservableProperty]
    private string _lastErrorSummary = "暂无";

    [ObservableProperty]
    private DateTimeOffset? _lastReadAt;

    [ObservableProperty]
    private int _lastReadCount;

    public void RecordRuntimeInfo(
        string manifestVersion,
        string pluginFolderPath,
        string settingsPath,
        string assemblyPath,
        DateTimeOffset? assemblyWriteTime)
    {
        var version = string.IsNullOrWhiteSpace(manifestVersion) ? "未知版本" : manifestVersion;
        var buildTime = assemblyWriteTime is { } value
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "未知构建时间";

        RuntimeSummary = $"{version} | {buildTime}";
        PluginFolderPath = string.IsNullOrWhiteSpace(pluginFolderPath) ? "未知" : pluginFolderPath;
        SettingsPath = string.IsNullOrWhiteSpace(settingsPath) ? "未知" : settingsPath;
        AssemblyPath = string.IsNullOrWhiteSpace(assemblyPath) ? "未知" : assemblyPath;
    }

    public void MarkStarted(bool enabled)
    {
        ListenerState = enabled ? "运行中" : "已启动，监听关闭";
    }

    public void MarkStopped()
    {
        ListenerState = "已停止";
    }

    public void MarkEnabledChanged(bool enabled)
    {
        ListenerState = enabled ? "运行中" : "监听关闭";
    }

    public void RecordPermission(string status)
    {
        PermissionStatus = string.IsNullOrWhiteSpace(status) ? "未知" : status;
    }

    public void RecordRead(int count, int unavailableAppIdentityCount)
    {
        LastReadAt = DateTimeOffset.Now;
        LastReadCount = count;

        LastReadSummary = unavailableAppIdentityCount > 0
            ? $"{LastReadAt:HH:mm:ss} 读取 {count} 条通知，{unavailableAppIdentityCount} 条因无法确认应用来源将被忽略"
            : $"{LastReadAt:HH:mm:ss} 读取 {count} 条通知";

        if (count > 0)
        {
            AppIdentityStatus = unavailableAppIdentityCount > 0
                ? "AppInfo 不可用，无法确认应用来源，相关通知将被忽略"
                : "AppInfo 可用";
        }
    }

    public void RecordCandidateFilter(int totalCount, int candidateCount, bool qqOnly)
    {
        LastCandidateSummary = qqOnly
            ? $"{DateTimeOffset.Now:HH:mm:ss} QQOnly 开启，{candidateCount}/{totalCount} 条进入解析"
            : $"{DateTimeOffset.Now:HH:mm:ss} QQOnly 关闭，处理 {candidateCount}/{totalCount} 条通知";
    }

    public void RecordAppIdentityFallback(string reason)
    {
        AppIdentityStatus = string.IsNullOrWhiteSpace(reason)
            ? "AppInfo 不可用，无法确认应用来源，相关通知将被忽略"
            : reason;
    }

    public void RecordAccepted(MessageEvent messageEvent, string reason)
    {
        LastAcceptedSummary = $"{messageEvent.ReceivedAt:HH:mm:ss} {FormatImportance(messageEvent.Importance)}（{reason}）";
        LastIgnoredReason = "暂无";
    }

    public void RecordIgnored(string reason)
    {
        LastIgnoredReason = string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason;
    }

    public void RecordFormatWarning(string reason)
    {
        LastFormatSummary = string.IsNullOrWhiteSpace(reason) ? "未知格式" : reason;
    }

    public void RecordError(Exception exception, string context)
    {
        LastErrorSummary = $"{DateTimeOffset.Now:HH:mm:ss} {context}: {exception.Message}";
    }

    public void RecordSettingsWarning(string warning)
    {
        LastErrorSummary = $"{DateTimeOffset.Now:HH:mm:ss} 设置恢复: {warning}";
    }

    public void ClearError()
    {
        LastErrorSummary = "暂无";
    }

    private static string FormatImportance(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => "呼叫",
        MessageImportance.Important => "重要",
        MessageImportance.Normal => "普通",
        MessageImportance.Ignored => "忽略",
        _ => importance.ToString()
    };
}
