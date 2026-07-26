using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public sealed class MessageAutomationEventArgs(MessageImportance importance) : EventArgs
{
    public MessageImportance Importance { get; } = importance;
}

public sealed class MessageAutomationEventHub(ILogger<MessageAutomationEventHub> logger)
{
    internal event EventHandler<MessageAutomationEventArgs>? MessageReceived;

    public void Publish(MessageEvent messageEvent)
    {
        if (messageEvent.Importance == MessageImportance.Ignored) return;

        var args = new MessageAutomationEventArgs(messageEvent.Importance);
        foreach (EventHandler<MessageAutomationEventArgs> handler in
                 MessageReceived?.GetInvocationList().Cast<EventHandler<MessageAutomationEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "QQListener 自动化订阅者执行失败。SubscriberType={SubscriberType} ExceptionType={ExceptionType}",
                    handler.Target?.GetType().FullName ?? "static",
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
        }
    }
}
