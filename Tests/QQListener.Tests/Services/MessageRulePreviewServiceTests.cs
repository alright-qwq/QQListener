using QQListener.Models;
using QQListener.Services;
using QQListener.Tests.TestSupport;
using Xunit;

namespace QQListener.Tests.Services;

public sealed class MessageRulePreviewServiceTests
{
    [Fact]
    public void PreviewUsesParserAndEvaluatorWithoutTouchingRuntimeState()
    {
        var settings = new QqListenerSettings { ImportantKeywordsText = "作业" };
        var latest = new LatestMessageState();
        var diagnostics = new QqListenerDiagnosticsState();
        var service = CreateService(settings);

        var result = service.Preview("Alice: 作业", "qq.toast", "12:30");

        Assert.True(result.Success);
        Assert.False(result.IsIgnored);
        Assert.Equal(MessageImportance.Important, result.Importance);
        Assert.Equal("命中重要关键词", result.Reason);
        Assert.False(latest.HasMessage);
        Assert.Equal("暂无", diagnostics.LastAcceptedSummary);
    }

    [Fact]
    public void InvalidPreviewTimeReturnsFailureInsteadOfMutatingSettings()
    {
        var settings = new QqListenerSettings { RuleTimeWindowStartText = "08:00" };
        var service = CreateService(settings);

        var result = service.Preview("Alice: hello", "qq.toast", "25:99");

        Assert.False(result.Success);
        Assert.Equal("时间格式必须为 HH:mm", result.Reason);
        Assert.Equal("08:00", settings.RuleTimeWindowStartText);
    }

    [Fact]
    public void PreviewDefaultsBlankSourceAndTimeWithoutExposingMessageContent()
    {
        var settings = new QqListenerSettings { AllowedSourcesText = "qq.toast" };
        var service = CreateService(settings);

        var result = service.Preview("Alice: confidential body", "", null);

        Assert.True(result.Success);
        Assert.Equal(MessageImportance.Normal, result.Importance);
        Assert.Equal("未命中特殊规则", result.Reason);
        Assert.DoesNotContain("Alice", result.ToString());
        Assert.DoesNotContain("confidential", result.ToString());
    }

    private static MessageRulePreviewService CreateService(QqListenerSettings settings) => new(
        new QqToastMessageParser(),
        new MessageRuleEvaluator(settings),
        new ManualTimeProvider(DateTimeOffset.Parse("2026-07-26T12:00:00+08:00")));
}
