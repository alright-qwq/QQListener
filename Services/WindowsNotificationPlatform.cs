using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using QQListener.Models;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QQListener.Services;

public sealed class WindowsNotificationPlatform(
    QqListenerDiagnosticsState diagnostics,
    ILogger<WindowsNotificationPlatform> logger) : IWindowsNotificationPlatform
{
    private volatile bool _appInfoUnsupported;

    public async Task<PlatformAccessStatus> RequestAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PlatformAccessStatus status;
        if (Dispatcher.UIThread.CheckAccess())
        {
            status = await RequestAccessOnUiThreadAsync(cancellationToken);
        }
        else
        {
            var requestTask = await Dispatcher.UIThread.InvokeAsync(
                () => RequestAccessOnUiThreadAsync(cancellationToken),
                DispatcherPriority.Normal,
                cancellationToken);
            status = await requestTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return status;
    }

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var listener = GetListener(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<WindowsToastSnapshot>();
        foreach (var notification in notifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = ReadSnapshot(notification);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Texts.Count > 0)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private async Task<PlatformAccessStatus> RequestAccessOnUiThreadAsync(
        CancellationToken cancellationToken)
    {
        var listener = GetListener(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var status = await listener.RequestAccessAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return status switch
        {
            UserNotificationListenerAccessStatus.Allowed => PlatformAccessStatus.Allowed,
            UserNotificationListenerAccessStatus.Denied => PlatformAccessStatus.Denied,
            _ => PlatformAccessStatus.Unspecified
        };
    }

    private static UserNotificationListener GetListener(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        UserNotificationListener listener;
        try
        {
            listener = UserNotificationListener.Current;
        }
        catch (Exception ex)
        {
            throw new WindowsNotificationListenerUnavailableException(
                "无法获取 UserNotificationListener.Current。",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return listener;
    }

    private WindowsToastSnapshot ReadSnapshot(UserNotification notification)
    {
        var texts = new List<string>();
        var isAppIdentityAvailable = ReadAppIdentity(notification, out var appName, out var appId);

        try
        {
            foreach (var binding in notification.Notification.Visual.Bindings)
            {
                foreach (var textElement in binding.GetTextElements())
                {
                    var text = textElement.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        texts.Add(text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取通知文本内容失败。");
            return new WindowsToastSnapshot(notification.Id, appName, appId, isAppIdentityAvailable, []);
        }

        return new WindowsToastSnapshot(
            notification.Id,
            appName,
            appId,
            isAppIdentityAvailable,
            texts.ToArray());
    }

    private bool ReadAppIdentity(UserNotification notification, out string appName, out string appId)
    {
        appName = "";
        appId = "";

        if (_appInfoUnsupported)
        {
            // 已确认本进程拿不到 AppInfo，无法确认应用来源；后续筛选将忽略该通知。
            return false;
        }

        try
        {
            var appInfo = notification.AppInfo;
            appName = appInfo?.DisplayInfo?.DisplayName ?? "";
            appId = appInfo?.AppUserModelId ?? "";
            return true;
        }
        catch (NotImplementedException)
        {
            MarkAppInfoUnsupported();
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "读取通知应用身份失败，无法确认应用来源，本条通知将被忽略。");
            diagnostics.RecordAppIdentityFallback($"读取 AppInfo 失败，无法确认应用来源，本条通知将被忽略: {ex.GetType().Name}");
            return false;
        }
    }

    private void MarkAppInfoUnsupported()
    {
        if (_appInfoUnsupported) return;

        _appInfoUnsupported = true;
        logger.LogInformation(
            "当前运行环境不支持读取 UserNotification.AppInfo（未打包的桌面应用即如此），无法确认通知应用来源；相关通知将被忽略。");
        diagnostics.RecordAppIdentityFallback("AppInfo 不可用（unpackaged 应用限制），无法确认应用来源，相关通知将被忽略");
    }
}
