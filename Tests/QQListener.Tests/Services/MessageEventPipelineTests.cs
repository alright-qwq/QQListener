using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Models;
using QQListener.Services;
using QQListener.Tests.TestSupport;
using Xunit;

namespace QQListener.Tests.Services;

public sealed class MessageEventPipelineTests
{
    [Fact]
    public void CleanupNeverExpiresBeforeConfiguredCooldown()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-17T00:00:00Z"));
        var settings = new QqListenerSettings { CooldownSeconds = 3600 };
        var pipeline = CreatePipeline(settings, time);
        Assert.True(pipeline.Process(Event("A")).Accepted);

        time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(pipeline.Process(Event("B")).Accepted);
        Assert.False(pipeline.Process(Event("A")).Accepted);

        time.Advance(TimeSpan.FromMinutes(50));
        Assert.True(pipeline.Process(Event("A")).Accepted);
    }

    [Fact]
    public void CooldownUsesExactConfiguredBoundary()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-17T00:00:00Z"));
        var settings = new QqListenerSettings { CooldownSeconds = 3600 };
        var pipeline = CreatePipeline(settings, time);
        Assert.True(pipeline.Process(Event("A")).Accepted);

        time.Advance(TimeSpan.FromSeconds(3599.999));
        Assert.False(pipeline.Process(Event("A")).Accepted);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(pipeline.Process(Event("A")).Accepted);
    }

    private static MessageEventPipeline CreatePipeline(
        QqListenerSettings settings,
        TimeProvider timeProvider)
    {
        return new MessageEventPipeline(
            settings,
            new MessageRuleEvaluator(settings),
            new QqListenerDiagnosticsState(),
            NullLogger<MessageEventPipeline>.Instance,
            timeProvider);
    }

    private static MessageEvent Event(string key) => new(
        "qq.toast",
        "QQ",
        "Alice",
        "",
        key,
        DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
        MessageImportance.Normal,
        ["Alice", key],
        $"qq.toast:{key}");
}
