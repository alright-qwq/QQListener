using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Models;
using QQListener.Services;
using QQListener.Tests.TestSupport;
using Xunit;

namespace QQListener.Tests.Services;

public class WindowsNotificationReaderTests
{
    [Fact]
    public async Task PermissionDeniedIsNotRequestedAgainWithoutExplicitRetry()
    {
        var platform = new FakeWindowsNotificationPlatform { AccessStatus = PlatformAccessStatus.Denied };
        var reader = CreateReader(platform);

        Assert.Equal(WindowsNotificationReadStatus.PermissionDenied,
            (await reader.ReadToastNotificationsAsync(false, default)).Status);
        Assert.Equal(WindowsNotificationReadStatus.PermissionDenied,
            (await reader.ReadToastNotificationsAsync(false, default)).Status);
        Assert.Equal(1, platform.RequestAccessCount);

        await reader.ReadToastNotificationsAsync(true, default);
        Assert.Equal(2, platform.RequestAccessCount);
    }

    [Fact]
    public async Task UnspecifiedAccessIsNotRequestedAgainWithoutExplicitRetry()
    {
        var platform = new FakeWindowsNotificationPlatform { AccessStatus = PlatformAccessStatus.Unspecified };
        var reader = CreateReader(platform);

        var first = await reader.ReadToastNotificationsAsync(false, default);
        var second = await reader.ReadToastNotificationsAsync(false, default);

        Assert.Equal(WindowsNotificationReadStatus.PermissionDenied, first.Status);
        Assert.Equal(WindowsNotificationReadStatus.PermissionDenied, second.Status);
        Assert.Equal(1, platform.RequestAccessCount);
    }

    [Fact]
    public async Task ConcurrentReadsShareOnePermissionRequest()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new FakeWindowsNotificationPlatform
        {
            RequestAccessHandler = async cancellationToken =>
            {
                requestStarted.TrySetResult();
                await releaseRequest.Task.WaitAsync(cancellationToken);
                return PlatformAccessStatus.Allowed;
            }
        };
        var reader = CreateReader(platform);

        try
        {
            var first = reader.ReadToastNotificationsAsync(false, default);
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = reader.ReadToastNotificationsAsync(false, default);

            releaseRequest.TrySetResult();
            var results = await Task.WhenAll(first, second);

            Assert.All(results, result => Assert.Equal(WindowsNotificationReadStatus.Success, result.Status));
            Assert.Equal(1, platform.RequestAccessCount);
        }
        finally
        {
            releaseRequest.TrySetResult();
        }
    }

    [Fact]
    public async Task ReadFailureIsDifferentFromSuccessfulEmptyRead()
    {
        var failed = await CreateReader(FakeWindowsNotificationPlatform.FailingRead())
            .ReadToastNotificationsAsync(false, default);
        var empty = await CreateReader(FakeWindowsNotificationPlatform.Empty())
            .ReadToastNotificationsAsync(false, default);

        Assert.Equal(WindowsNotificationReadStatus.ReadFailed, failed.Status);
        Assert.Equal(WindowsNotificationReadStatus.Success, empty.Status);
        Assert.Empty(empty.Notifications);
    }

    [Fact]
    public async Task ListenerUnavailableIsReportedSeparatelyFromReadFailure()
    {
        var result = await CreateReader(FakeWindowsNotificationPlatform.ListenerUnavailable())
            .ReadToastNotificationsAsync(false, default);

        Assert.Equal(WindowsNotificationReadStatus.ListenerUnavailable, result.Status);
        Assert.IsType<WindowsNotificationListenerUnavailableException>(result.Error);
        Assert.Empty(result.Notifications);
    }

    [Fact]
    public async Task ReadFailureDoesNotResetPreviouslyAllowedPermission()
    {
        var platform = FakeWindowsNotificationPlatform.FailingRead();
        var reader = CreateReader(platform);

        var first = await reader.ReadToastNotificationsAsync(false, default);
        var second = await reader.ReadToastNotificationsAsync(false, default);

        Assert.Equal(WindowsNotificationReadStatus.ReadFailed, first.Status);
        Assert.Equal(WindowsNotificationReadStatus.ReadFailed, second.Status);
        Assert.Equal(1, platform.RequestAccessCount);
        Assert.Equal(2, platform.ReadCount);
    }

    [Theory]
    [InlineData(true, WindowsNotificationReadStatus.ListenerUnavailable)]
    [InlineData(false, WindowsNotificationReadStatus.ReadFailed)]
    public async Task FailedPermissionRequestIsRetriedByNextBackgroundRead(
        bool listenerUnavailable,
        WindowsNotificationReadStatus expectedFailure)
    {
        var platform = new FakeWindowsNotificationPlatform
        {
            RequestAccessException = listenerUnavailable
                ? new WindowsNotificationListenerUnavailableException("listener unavailable")
                : new InvalidOperationException("request failed")
        };
        var reader = CreateReader(platform);

        var failed = await reader.ReadToastNotificationsAsync(false, default);
        platform.RequestAccessException = null;
        var recovered = await reader.ReadToastNotificationsAsync(false, default);

        Assert.Equal(expectedFailure, failed.Status);
        Assert.Equal(WindowsNotificationReadStatus.Success, recovered.Status);
        Assert.Equal(2, platform.RequestAccessCount);
    }

    [Theory]
    [InlineData(PlatformAccessStatus.Denied, false, WindowsNotificationReadStatus.ReadFailed)]
    [InlineData(PlatformAccessStatus.Denied, true, WindowsNotificationReadStatus.ListenerUnavailable)]
    [InlineData(PlatformAccessStatus.Unspecified, false, WindowsNotificationReadStatus.ReadFailed)]
    [InlineData(PlatformAccessStatus.Unspecified, true, WindowsNotificationReadStatus.ListenerUnavailable)]
    public async Task TransientExplicitRetryFailureClearsPreviousPermissionLatch(
        PlatformAccessStatus initialAccessStatus,
        bool listenerUnavailable,
        WindowsNotificationReadStatus expectedRetryFailure)
    {
        var platform = new FakeWindowsNotificationPlatform { AccessStatus = initialAccessStatus };
        var reader = CreateReader(platform);

        var denied = await reader.ReadToastNotificationsAsync(false, default);

        platform.RequestAccessException = listenerUnavailable
            ? new WindowsNotificationListenerUnavailableException("listener unavailable")
            : new InvalidOperationException("request failed");
        var retryFailed = await reader.ReadToastNotificationsAsync(true, default);

        platform.RequestAccessException = null;
        platform.AccessStatus = PlatformAccessStatus.Allowed;
        var recovered = await reader.ReadToastNotificationsAsync(false, default);

        Assert.Equal(WindowsNotificationReadStatus.PermissionDenied, denied.Status);
        Assert.Equal(expectedRetryFailure, retryFailed.Status);
        Assert.Equal(WindowsNotificationReadStatus.Success, recovered.Status);
        Assert.Equal(3, platform.RequestAccessCount);
    }

    private static WindowsNotificationReader CreateReader(IWindowsNotificationPlatform platform)
    {
        return new WindowsNotificationReader(
            platform,
            new QqListenerDiagnosticsState(),
            NullLogger<WindowsNotificationReader>.Instance);
    }
}
