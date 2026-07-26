using System.Globalization;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public sealed class MessageRuleEvaluatorTests
{
    [Fact]
    public void FixedPriorityIsBlacklistThenSourceThenTimeThenImportance()
    {
        var settings = new QqListenerSettings
        {
            BlacklistText = "secret-blacklist",
            AllowedSourcesText = "allowed.source",
            RuleTimeWindowEnabled = true,
            RuleTimeWindowStartText = "08:00",
            RuleTimeWindowEndText = "18:00",
            CallingEnabled = true,
            CallingKeyword = "call"
        };
        var evaluator = new MessageRuleEvaluator(settings);

        Assert.Equal("命中黑名单规则", evaluator.Evaluate(Event("other", "secret-blacklist call", "12:00")).Reason);
        Assert.Equal("来源不在允许列表", evaluator.Evaluate(Event("other", "call", "12:00")).Reason);
        Assert.Equal("不在允许时间段", evaluator.Evaluate(Event("allowed.source", "call", "20:00")).Reason);
        Assert.Equal(MessageImportance.Calling, evaluator.Evaluate(Event("allowed.source", "call", "12:00")).Event.Importance);
    }

    [Fact]
    public void EmptyAllowedSourcesAllowsEverySource()
    {
        var result = new MessageRuleEvaluator(new QqListenerSettings()).Evaluate(Event("unknown.source", "normal", "12:00"));

        Assert.False(result.IsIgnored);
        Assert.Equal(MessageImportance.Normal, result.Event.Importance);
    }

    [Theory]
    [InlineData("QQ.Toast", "qq.toast", false)]
    [InlineData("qq.toast.extra", "qq.toast", true)]
    public void AllowedSourcesUsesExactCaseInsensitiveMatching(string source, string allowedSources, bool ignored)
    {
        var settings = new QqListenerSettings { AllowedSourcesText = allowedSources };
        var result = new MessageRuleEvaluator(settings).Evaluate(Event(source, "normal", "12:00"));

        Assert.Equal(ignored, result.IsIgnored);
        Assert.Equal(ignored ? "来源不在允许列表" : "未命中特殊规则", result.Reason);
    }

    [Theory]
    [InlineData("08:00", true)]
    [InlineData("17:59", true)]
    [InlineData("18:00", false)]
    public void DayWindowUsesInclusiveStartExclusiveEnd(string clock, bool accepted)
    {
        Assert.Equal(accepted, !EvaluateAt("08:00", "18:00", clock).IsIgnored);
    }

    [Theory]
    [InlineData("22:00", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    public void OvernightWindowWrapsMidnight(string clock, bool accepted)
    {
        Assert.Equal(accepted, !EvaluateAt("22:00", "07:00", clock).IsIgnored);
    }

    [Theory]
    [InlineData("00:00")]
    [InlineData("12:00")]
    [InlineData("23:59")]
    public void EqualTimeBoundsAllowTheWholeDay(string clock)
    {
        Assert.False(EvaluateAt("08:00", "08:00", clock).IsIgnored);
    }

    private static MessageEvent Event(string source, string body, string clock)
    {
        var time = TimeOnly.ParseExact(clock, "HH:mm", CultureInfo.InvariantCulture);
        return new MessageEvent(
            source, "QQ", "Alice", "班群", body,
            new DateTimeOffset(2026, 7, 26, time.Hour, time.Minute, 0, TimeSpan.FromHours(8)),
            MessageImportance.Normal, ["Alice", body], $"{source}:{clock}:{body}");
    }

    private static MessageRuleEvaluationResult EvaluateAt(string start, string end, string clock)
    {
        var settings = new QqListenerSettings
        {
            RuleTimeWindowEnabled = true,
            RuleTimeWindowStartText = start,
            RuleTimeWindowEndText = end
        };
        settings.Normalize();
        return new MessageRuleEvaluator(settings).Evaluate(Event("qq.toast", "normal", clock));
    }
}
