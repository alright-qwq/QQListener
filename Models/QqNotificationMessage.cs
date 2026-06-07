namespace QQListener.Models;

public sealed record QqNotificationMessage(
    string Sender,
    string Message,
    bool Important,
    bool Calling,
    TimeSpan Duration)
{
    public static QqNotificationMessage FromMessageEvent(MessageEvent messageEvent, TimeSpan duration) =>
        new(
            messageEvent.Sender,
            messageEvent.Body,
            messageEvent.IsImportant,
            messageEvent.IsCalling,
            duration);
}
