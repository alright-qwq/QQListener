namespace QQListener.Services;

public interface IWindowsNotificationReader
{
    Task<WindowsNotificationReadResult> ReadToastNotificationsAsync(
        bool retryPermission,
        CancellationToken cancellationToken);
}
