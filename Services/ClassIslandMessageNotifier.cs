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
        var duration = GetDuration(messageEvent.Importance);
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

    public TimeSpan GetDuration(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => TimeSpan.FromSeconds(
            Math.Clamp(settings.CallingDurationSeconds, 1, 86400)),
        MessageImportance.Important => TimeSpan.FromSeconds(
            Math.Clamp(settings.ImportantDurationSeconds, 1, 3600)),
        _ => TimeSpan.FromSeconds(
            Math.Clamp(settings.NormalDurationSeconds, 1, 3600))
    };
}
