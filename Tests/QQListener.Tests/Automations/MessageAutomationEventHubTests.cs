using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Automations;

public sealed class MessageAutomationEventHubTests
{
    [Fact]
    public void PublishContinuesAfterSubscriberFailure()
    {
        var hub = new MessageAutomationEventHub(NullLogger<MessageAutomationEventHub>.Instance);
        var received = 0;
        hub.MessageReceived += (_, _) => throw new InvalidOperationException("boom");
        hub.MessageReceived += (_, _) => received++;

        hub.Publish(Event(MessageImportance.Important));

        Assert.Equal(1, received);
    }

    [Fact]
    public void PublishDoesNotNotifySubscribersForIgnoredMessages()
    {
        var hub = new MessageAutomationEventHub(NullLogger<MessageAutomationEventHub>.Instance);
        var received = 0;
        hub.MessageReceived += (_, _) => received++;

        hub.Publish(Event(MessageImportance.Ignored));

        Assert.Equal(0, received);
    }

    private static MessageEvent Event(MessageImportance importance) => new(
        "qq.toast",
        "QQ",
        "Alice",
        "",
        "message",
        DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
        importance,
        ["Alice", "message"],
        "qq.toast:message");
}
