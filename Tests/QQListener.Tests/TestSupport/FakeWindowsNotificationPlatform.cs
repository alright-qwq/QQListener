using QQListener.Services;

namespace QQListener.Tests.TestSupport;

public sealed class FakeWindowsNotificationPlatform : IWindowsNotificationPlatform
{
    public PlatformAccessStatus AccessStatus { get; set; } = PlatformAccessStatus.Allowed;

    public IReadOnlyList<WindowsToastSnapshot> Notifications { get; set; } = [];

    public Exception? RequestAccessException { get; set; }

    public Exception? ReadException { get; set; }

    public Func<CancellationToken, Task<PlatformAccessStatus>>? RequestAccessHandler { get; set; }

    public Func<CancellationToken, Task<IReadOnlyList<WindowsToastSnapshot>>>? ReadHandler { get; set; }

    public int RequestAccessCount { get; private set; }

    public int ReadCount { get; private set; }

    public async Task<PlatformAccessStatus> RequestAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestAccessCount++;

        if (RequestAccessException is not null)
        {
            throw RequestAccessException;
        }

        return RequestAccessHandler is null
            ? AccessStatus
            : await RequestAccessHandler(cancellationToken);
    }

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;

        if (ReadException is not null)
        {
            throw ReadException;
        }

        return ReadHandler is null
            ? Notifications
            : await ReadHandler(cancellationToken);
    }

    public static FakeWindowsNotificationPlatform FailingRead()
    {
        return new FakeWindowsNotificationPlatform
        {
            ReadException = new InvalidOperationException("read failed")
        };
    }

    public static FakeWindowsNotificationPlatform Empty()
    {
        return new FakeWindowsNotificationPlatform();
    }

    public static FakeWindowsNotificationPlatform ListenerUnavailable()
    {
        return new FakeWindowsNotificationPlatform
        {
            ReadException = new WindowsNotificationListenerUnavailableException("listener unavailable")
        };
    }
}
