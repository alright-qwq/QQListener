using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QQListener.Services;

public sealed class WindowsNotificationReader
{
    private UserNotificationListenerAccessStatus _accessStatus = UserNotificationListenerAccessStatus.Unspecified;

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync()
    {
        var listener = UserNotificationListener.Current;
        if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            _accessStatus = await listener.RequestAccessAsync();
            if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                return [];
            }
        }

        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        return notifications
            .Select(ReadSnapshot)
            .Where(x => x.Texts.Count > 0)
            .ToArray();
    }

    private static WindowsToastSnapshot ReadSnapshot(UserNotification notification)
    {
        var texts = new List<string>();
        var appName = "";

        try
        {
            appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "";
        }
        catch
        {
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
        catch
        {
            return new WindowsToastSnapshot(notification.Id, appName, []);
        }

        return new WindowsToastSnapshot(notification.Id, appName, texts.Distinct().ToArray());
    }
}

public sealed record WindowsToastSnapshot(uint Id, string AppName, IReadOnlyList<string> Texts);
