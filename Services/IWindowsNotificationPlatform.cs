namespace QQListener.Services;

public enum PlatformAccessStatus
{
    Unspecified,
    Allowed,
    Denied
}

public interface IWindowsNotificationPlatform
{
    Task<PlatformAccessStatus> RequestAccessAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync(
        CancellationToken cancellationToken);
}

public sealed class WindowsNotificationListenerUnavailableException : Exception
{
    public WindowsNotificationListenerUnavailableException(string message)
        : base(message)
    {
    }

    public WindowsNotificationListenerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
