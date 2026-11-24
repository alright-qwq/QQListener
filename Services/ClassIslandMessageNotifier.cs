using Avalonia.Threading;
using ClassIsland.Core.Models.Notification;
using QQListener.Models;

namespace QQListener.Services;

public class ClassIslandMessageNotifier(QqListenerSettings settings)
{
    public void Show(MessageEvent messageEvent, Action<NotificationRequest> showNotification)
    {
        void DoShow()
        {
            showNotification(CreateRequest(messageEvent));
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            DoShow();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(DoShow);
    }

    public async Task ShowAsync(
        MessageEvent messageEvent,
        Action<NotificationRequest> showNotification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        void DoShow()
        {
            cancellationToken.ThrowIfCancellationRequested();
            showNotification(CreateRequest(messageEvent));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            DoShow();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(
            DoShow,
            DispatcherPriority.Normal,
            cancellationToken);
    }

    public NotificationRequest CreateRequest(MessageEvent messageEvent)
    {
        var duration = GetDuration(messageEvent);
        NotificationContent overlay;

        if (settings.RollingSpeed <= 0)
        {
            overlay = NotificationContent.CreateSimpleTextContent(messageEvent.DisplayBody, content =>
            {
                content.Duration = duration;
                content.SpeechContent = messageEvent.DisplayBody;
            });
        }
        else
        {
            // RollingSpeed 已被 Normalize() 夹紧到 (0, 4]，这里不再做冗余防御。
            var rollingDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / settings.RollingSpeed);
            overlay = NotificationContent.CreateRollingTextContent(messageEvent.DisplayBody, rollingDuration, messageEvent.IsCalling ? 10 : 2);
        }

        return new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(messageEvent.DisplayTitle, rightIcon: "\uE02B", factory: content =>
            {
                content.Duration = TimeSpan.FromSeconds(2);
                content.SpeechContent = messageEvent.DisplayTitle;
            }),
            OverlayContent = overlay,
            RequestNotificationSettings =
            {
                IsSettingsEnabled = true,
                IsSpeechEnabled = true,
                IsNotificationEffectEnabled = true,
                IsNotificationSoundEnabled = true,
                IsNotificationTopmostEnabled = GetTopmost(messageEvent.Importance)
            }
        };
    }

    public bool GetTopmost(MessageImportance importance) => importance switch
    {
        MessageImportance.Ignored => false,
        MessageImportance.Calling => settings.CallingTopmost,
        MessageImportance.Important => settings.ImportantTopmost,
        _ => settings.NormalTopmost
    };

    /// <summary>
    /// 计算消息展示时长：按单行文本长度自适应，配置的时长作为上限。
    /// 呼叫类提醒保持配置时长不变（用于持续提醒，不按字数缩短）。
    /// </summary>
    public TimeSpan GetDuration(MessageEvent messageEvent)
    {
        var maxSeconds = GetMaxDurationSeconds(messageEvent.Importance);

        if (messageEvent.Importance == MessageImportance.Calling)
        {
            return TimeSpan.FromSeconds(maxSeconds);
        }

        // 阅读速度约 6~7 字/秒，留出基础停留时间；长消息由配置上限截断。
        var textLength = messageEvent.DisplayBody.Length;
        var seconds = 1.5 + textLength * 0.15;
        var minSeconds = messageEvent.Importance == MessageImportance.Important ? 3.0 : 2.0;
        // 配置上限可能小于最短停留时间，取两者较小值兜底。
        var floorSeconds = Math.Min(minSeconds, maxSeconds);
        return TimeSpan.FromSeconds(Math.Clamp(seconds, floorSeconds, maxSeconds));
    }

    private double GetMaxDurationSeconds(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => Math.Clamp(settings.CallingDurationSeconds, 1, 86400),
        MessageImportance.Important => Math.Clamp(settings.ImportantDurationSeconds, 1, 3600),
        _ => Math.Clamp(settings.NormalDurationSeconds, 1, 3600)
    };
}
