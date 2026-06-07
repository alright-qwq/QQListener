using Microsoft.Extensions.Logging;
using QQListener.Models;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QQListener.Services;

public sealed class WindowsNotificationReader(
    QqListenerDiagnosticsState diagnostics,
    ILogger<WindowsNotificationReader> logger)
{
    private UserNotificationListenerAccessStatus _accessStatus = UserNotificationListenerAccessStatus.Unspecified;
    private volatile bool _hasLoggedUnavailableAppInfo;

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync()
    {
        var listener = UserNotificationListener.Current;
        if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            logger.LogInformation("正在请求通知监听权限...");
            _accessStatus = await listener.RequestAccessAsync();
            diagnostics.RecordPermission(_accessStatus.ToString());
            if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                logger.LogWarning("通知监听权限被拒绝（状态={Status}），无法读取通知。", _accessStatus);
                return [];
            }

            logger.LogInformation("已获取通知监听权限。");
        }

        try
        {
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var snapshots = notifications
                .Select(ReadSnapshot)
                .Where(x => x.Texts.Count > 0)
                .ToArray();
            diagnostics.RecordRead(snapshots.Length);
            diagnostics.ClearError();
            return snapshots;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取通知失败，将在下次轮询时重新请求权限。");
            diagnostics.RecordError(ex, "读取通知失败");
            _accessStatus = UserNotificationListenerAccessStatus.Unspecified;
            diagnostics.RecordPermission(_accessStatus.ToString());
            return [];
        }
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

        return new WindowsToastSnapshot(notification.Id, appName, appId, isAppIdentityAvailable, texts.ToArray());
    }

    private bool ReadAppIdentity(UserNotification notification, out string appName, out string appId)
    {
        appName = "";
        appId = "";

        try
        {
            var appInfo = notification.AppInfo;
            appName = appInfo?.DisplayInfo?.DisplayName ?? "";
            appId = appInfo?.AppUserModelId ?? "";
            return true;
        }
        catch (NotImplementedException ex)
        {
            LogUnavailableAppInfoOnce(ex);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "读取通知应用标识失败，已继续读取通知文本。");
            return false;
        }
    }

    private void LogUnavailableAppInfoOnce(Exception exception)
    {
        if (_hasLoggedUnavailableAppInfo) return;

        _hasLoggedUnavailableAppInfo = true;
        logger.LogDebug(exception, "当前运行环境不支持读取 UserNotification.AppInfo，已降级为仅使用通知文本识别。");
    }
}

public sealed record WindowsToastSnapshot(
    uint Id,
    string AppName,
    string AppId,
    bool IsAppIdentityAvailable,
    IReadOnlyList<string> Texts);
