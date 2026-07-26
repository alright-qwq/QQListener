using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Automations.Triggers;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Automations;

public sealed class MessageReceivedTriggerTests
{
    [Theory]
    [InlineData(MessageImportance.Normal, MessageImportance.Normal, 1)]
    [InlineData(MessageImportance.Normal, MessageImportance.Important, 0)]
    [InlineData(MessageImportance.Important, MessageImportance.Important, 1)]
    [InlineData(MessageImportance.Calling, MessageImportance.Calling, 1)]
    [InlineData(MessageImportance.Calling, MessageImportance.Ignored, 0)]
    public void LoadedTriggerMatchesExactlyOnce(
        MessageImportance target,
        MessageImportance published,
        int expected)
    {
        var hub = CreateHub();
        var trigger = new RecordingTrigger(hub, target);
        trigger.Loaded();
        trigger.Loaded();

        hub.Publish(Event(published));

        Assert.Equal(expected, trigger.Count);
        trigger.UnLoaded();
        trigger.UnLoaded();
        hub.Publish(Event(target));
        Assert.Equal(expected, trigger.Count);
    }

    private static MessageAutomationEventHub CreateHub() =>
        new(NullLogger<MessageAutomationEventHub>.Instance);

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

    private sealed class RecordingTrigger(
        MessageAutomationEventHub hub,
        MessageImportance target) : MessageReceivedTriggerBase(hub, target)
    {
        public int Count { get; private set; }

        protected override void RaiseTrigger() => Count++;
    }
}
