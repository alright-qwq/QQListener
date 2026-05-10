namespace QQListener.Models;

public sealed record QqNotificationMessage(
    string Sender,
    string Message,
    bool Important,
    bool Calling,
    TimeSpan Duration);
