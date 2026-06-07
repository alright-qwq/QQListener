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
            var rollingDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / Math.Max(0.01, settings.RollingSpeed));
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
                IsNotificationTopmostEnabled = messageEvent.IsCalling
            }
        };
    }

    public TimeSpan GetDuration(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => TimeSpan.FromSeconds(settings.CallingDurationSeconds),
        MessageImportance.Important => TimeSpan.FromSeconds(settings.ImportantDurationSeconds),
        _ => TimeSpan.FromSeconds(settings.NormalDurationSeconds)
    };
}
