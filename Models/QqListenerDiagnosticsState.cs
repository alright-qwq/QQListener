using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class QqListenerDiagnosticsState : ObservableObject
{
    [ObservableProperty]
    private string _listenerState = "未启动";

    [ObservableProperty]
    private string _permissionStatus = "未请求";

    [ObservableProperty]
    private string _lastReadSummary = "尚未读取";

    [ObservableProperty]
    private string _lastAcceptedSummary = "暂无";

    [ObservableProperty]
    private string _lastIgnoredReason = "暂无";

    [ObservableProperty]
    private string _lastErrorSummary = "暂无";

    [ObservableProperty]
    private DateTimeOffset? _lastReadAt;

    [ObservableProperty]
    private int _lastReadCount;

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

    public void RecordRead(int count)
    {
        LastReadAt = DateTimeOffset.Now;
        LastReadCount = count;
        LastReadSummary = $"{LastReadAt:HH:mm:ss} 读取 {count} 条通知";
    }

    public void RecordAccepted(MessageEvent messageEvent, string reason)
    {
        LastAcceptedSummary = $"{messageEvent.ReceivedAt:HH:mm:ss} {FormatImportance(messageEvent.Importance)} {messageEvent.Sender}: {Truncate(messageEvent.Body, 40)}（{reason}）";
        LastIgnoredReason = "暂无";
    }

    public void RecordIgnored(string reason)
    {
        LastIgnoredReason = string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason;
    }

    public void RecordError(Exception exception, string context)
    {
        LastErrorSummary = $"{DateTimeOffset.Now:HH:mm:ss} {context}: {exception.Message}";
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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
