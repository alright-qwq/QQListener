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
    // 一旦确认本进程拿不到 UserNotification.AppInfo（unpackaged 桌面 .NET 应用上
    // 这个 WinRT projection 必抛 NotImplementedException），就别再调那个 getter：
    // 一是避免每条 toast 都抛/捕一次拖累性能，二是避免堆栈污染日志。
    private volatile bool _appInfoUnsupported;

    public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync()
    {
        UserNotificationListener listener;
        try
        {
            listener = UserNotificationListener.Current;
        }
        catch (Exception ex)
        {
            // Win10 N/精简版或受限环境下 UserNotificationListener.Current 本身就可能
            // 抛 TypeInitialization/PlatformNotSupported/COMException。如果不接住，
            // 上层 PollOnceAsync 会每 0.3 秒崩一次。
            logger.LogWarning(ex, "无法获取 UserNotificationListener.Current，跳过本次轮询。");
            diagnostics.RecordError(ex, "获取通知监听器失败");
            return [];
        }

        if (_accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            try
            {
                logger.LogInformation("正在请求通知监听权限...");
                _accessStatus = await listener.RequestAccessAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "请求通知监听权限失败。");
                diagnostics.RecordError(ex, "请求通知监听权限失败");
                return [];
            }
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
            diagnostics.RecordRead(snapshots.Length, snapshots.Count(x => !x.IsAppIdentityAvailable));
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
            // 单条通知偶发的读取失败：不升级到"全局不支持"，下一条还可以再尝试。
            logger.LogDebug(ex, "读取通知应用身份失败，无法确认应用来源，本条通知将被忽略。");
            diagnostics.RecordAppIdentityFallback($"读取 AppInfo 失败，无法确认应用来源，本条通知将被忽略: {ex.GetType().Name}");
            return false;
        }
    }

    private void MarkAppInfoUnsupported()
    {
        if (_appInfoUnsupported) return;

        _appInfoUnsupported = true;
        // 这是预期的平台限制，不是 bug：unpackaged 的桌面 .NET 进程访问 UserNotification.AppInfo
        // 会抛 NotImplementedException。使用 Information 级别并明确相关通知将因来源未知被忽略。
        logger.LogInformation(
            "当前运行环境不支持读取 UserNotification.AppInfo（未打包的桌面应用即如此），无法确认通知应用来源；相关通知将被忽略。");
        diagnostics.RecordAppIdentityFallback("AppInfo 不可用（unpackaged 应用限制），无法确认应用来源，相关通知将被忽略");
    }
}

public sealed record WindowsToastSnapshot(
    uint Id,
    string AppName,
    string AppId,
    bool IsAppIdentityAvailable,
    IReadOnlyList<string> Texts);
