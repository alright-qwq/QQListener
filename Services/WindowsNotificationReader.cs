using Microsoft.Extensions.Logging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QQListener.Services;

public sealed class WindowsNotificationReader(ILogger<WindowsNotificationReader> logger)
{
    private UserNotificationListenerAccessStatus _accessStatus = UserNotificationListenerAccessStatus.Unspecified;

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync()
    {
        var listener = UserNotificationListener.Current;
        if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            logger.LogInformation("正在请求通知监听权限...");
            _accessStatus = await listener.RequestAccessAsync();
            if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                logger.LogWarning("通知监听权限被拒绝（状态={Status}），无法读取通知。", _accessStatus);
                return [];
            }

            logger.LogInformation("已获取通知监听权限。");
        }

        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        return notifications
            .Select(ReadSnapshot)
            .Where(x => x.Texts.Count > 0)
            .ToArray();
    }

    private WindowsToastSnapshot ReadSnapshot(UserNotification notification)
    {
        var texts = new List<string>();
        var appName = "";

        try
        {
            appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取通知应用名称失败。");
            appName = "";
        }

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
            return new WindowsToastSnapshot(notification.Id, appName, []);
        }

        return new WindowsToastSnapshot(notification.Id, appName, texts.Distinct().ToArray());
    }
}

public sealed record WindowsToastSnapshot(uint Id, string AppName, IReadOnlyList<string> Texts);
