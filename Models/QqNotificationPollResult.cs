using QQListener.Services;

namespace QQListener.Models;

public sealed record QqNotificationPollResult(
    WindowsNotificationReadStatus ReadStatus,
    IReadOnlyList<MessageEvent> AcceptedEvents,
    string Reason)
{
    public bool IsSuccess => ReadStatus == WindowsNotificationReadStatus.Success;
}
