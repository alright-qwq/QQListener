using QQListener.Models;

namespace QQListener.Services;

public interface IQqNotificationPoller
{
    Task<QqNotificationPollResult> PollAsync(
        bool retryPermission,
        CancellationToken cancellationToken);

    void ResetDetectionState();
}
