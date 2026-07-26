using Microsoft.Extensions.Logging;
using QQListener.Models;
using QQListener.Services;
using QQListener.Tests.TestSupport;
using Xunit;

namespace QQListener.Tests.Services;

public sealed class PrivacyTests
{
    private static readonly string[] Secrets =
    [
        "secret-sender",
        "private-body",
        "secret-blacklist",
        "secret-person",
        "secret-important",
        "secret-calling",
        "secret-source",
        "23:59"
    ];

    [Fact]
    public void RuleReasonsNeverContainPrivateRuleValues()
    {
        var settings = Settings();
        settings.AllowedSourcesText = "qq.toast";
        settings.RuleTimeWindowEnabled = true;
        settings.RuleTimeWindowStartText = "00:00";
        settings.RuleTimeWindowEndText = "23:59";
        var evaluator = new MessageRuleEvaluator(settings);

        var results = new[]
        {
            evaluator.Evaluate(Event("normal", "secret-blacklist", "blacklist")),
            evaluator.Evaluate(Event("normal", "normal", "source", source: "secret-source")),
            evaluator.Evaluate(Event("normal", "normal", "time", receivedAt: DateTimeOffset.Parse("2026-07-17T23:59:00Z"))),
            evaluator.Evaluate(Event("normal", "secret-calling", "calling")),
            evaluator.Evaluate(Event("secret-person", "normal", "person")),
            evaluator.Evaluate(Event("normal", "secret-important", "important")),
            evaluator.Evaluate(Event("normal", "有人@我", "at")),
            evaluator.Evaluate(Event("normal", "normal", "normal"))
        };

        Assert.Equal(
            [
                "命中黑名单规则",
                "来源不在允许列表",
                "不在允许时间段",
                "命中呼叫关键词",
                "命中重要人物规则",
                "命中重要关键词",
                "检测到有人@我",
                "未命中特殊规则"
            ],
            results.Select(result => result.Reason).ToArray());
        AssertRedacted(results.Select(result => result.Reason));
    }

    [Fact]
    public void DiagnosticsAndPipelineLogsNeverContainPrivateMessageOrRuleValues()
    {
        var settings = Settings();
        var diagnostics = new QqListenerDiagnosticsState();
        var logger = new ListLogger<MessageEventPipeline>();
        var pipeline = new MessageEventPipeline(
            settings,
            new MessageRuleEvaluator(settings),
            diagnostics,
            logger,
            TimeProvider.System);

        var accepted = pipeline.Process(Event("secret-person", "private-body", "accepted"));
        var ignored = pipeline.Process(Event("secret-sender", "secret-blacklist", "ignored"));

        Assert.True(accepted.Accepted);
        Assert.False(ignored.Accepted);
        AssertRedacted(
            logger.Entries
                .Append(diagnostics.LastAcceptedSummary)
                .Append(diagnostics.LastIgnoredReason)
                .Append(diagnostics.LastFormatSummary)
                .Append(accepted.Reason)
                .Append(ignored.Reason));
    }

    [Fact]
    public async Task PollerDegradedWarningIncludesFixedReasonWithoutPrivateContent()
    {
        var settings = Settings();
        settings.ImportantPersonsText = "";
        var diagnostics = new QqListenerDiagnosticsState();
        var pollerLogger = new ListLogger<QqNotificationPoller>();
        var pipelineLogger = new ListLogger<MessageEventPipeline>();
        var pipeline = new MessageEventPipeline(
            settings,
            new MessageRuleEvaluator(settings),
            diagnostics,
            pipelineLogger,
            TimeProvider.System);
        var reader = SequencedNotificationReader.Create(
            Success(),
            Success(new WindowsToastSnapshot(
                1,
                "QQ",
                "Tencent.QQNT",
                true,
                ["private-body"])));
        var poller = new QqNotificationPoller(
            settings,
            reader,
            new QqToastMessageParser(),
            pipeline,
            diagnostics,
            pollerLogger);

        await poller.PollAsync(false, default);
        var result = await poller.PollAsync(false, default);

        Assert.Single(result.AcceptedEvents);
        var logEntries = pollerLogger.Entries.Concat(pipelineLogger.Entries).ToArray();
        Assert.Contains(
            logEntries,
            entry => entry.Contains("Reason=未知格式，已降级解析", StringComparison.Ordinal));
        AssertRedacted(logEntries);
    }

    private static QqListenerSettings Settings() => new()
    {
        IsEnabled = true,
        BlacklistText = "secret-blacklist",
        ImportantPersonsText = "secret-person",
        ImportantKeywordsText = "secret-important",
        CallingKeyword = "secret-calling",
        CallingEnabled = true,
        SomeoneAtMe = true
    };

    private static MessageEvent Event(
        string sender,
        string body,
        string key,
        string source = "qq.toast",
        DateTimeOffset? receivedAt = null) => new(
        source,
        "QQ",
        sender,
        "private-conversation",
        body,
        receivedAt ?? DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
        MessageImportance.Normal,
        [sender, body],
        $"{source}:{key}");

    private static WindowsNotificationReadResult Success(params WindowsToastSnapshot[] snapshots) =>
        new(WindowsNotificationReadStatus.Success, snapshots, "success");

    private static void AssertRedacted(IEnumerable<string> values)
    {
        var text = string.Join("\n", values);
        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel is LogLevel.Debug or LogLevel.Information or LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Entries.Add(formatter(state, exception));
            }
        }
    }
}
