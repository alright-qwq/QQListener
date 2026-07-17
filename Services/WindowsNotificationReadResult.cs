namespace QQListener.Services;

public enum WindowsNotificationReadStatus
{
    Success,
    PermissionDenied,
    ListenerUnavailable,
    ReadFailed
}

public sealed record WindowsNotificationReadResult(
    WindowsNotificationReadStatus Status,
    IReadOnlyList<WindowsToastSnapshot> Notifications,
    string Reason,
    Exception? Error = null)
{
    public bool IsSuccess => Status == WindowsNotificationReadStatus.Success;
}
