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

    [Fact]
    public void SubscriberFailureLogContainsTypesButNoPrivatePayloadOrExceptionMessage()
    {
        var logger = new RecordingLogger<MessageAutomationEventHub>();
        var hub = new MessageAutomationEventHub(logger);
        hub.MessageReceived += (_, _) =>
            throw new InvalidOperationException("secret-exception-message");

        hub.Publish(new MessageEvent(
            "secret-source", "QQ", "secret-sender", "secret-conversation",
            "private-body", DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            MessageImportance.Important, ["secret-sender", "private-body"],
            "secret-deduplication-key"));

        var text = Assert.Single(logger.Messages);
        Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-exception-message", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-sender", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-conversation", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-body", text, StringComparison.Ordinal);
        Assert.All(logger.Exceptions, exception => Assert.Null(exception));
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
