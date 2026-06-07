using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public class MessageEventPipeline(
    QqListenerSettings settings,
    MessageRuleEvaluator ruleEvaluator,
    QqListenerDiagnosticsState diagnostics,
    ILogger<MessageEventPipeline> logger)
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private HashSet<string> _activeDeduplicationKeys = [];
    private readonly object _lock = new();

    public MessagePipelineResult Process(MessageEvent messageEvent)
    {
        var now = DateTimeOffset.Now;

        lock (_lock)
        {
            if (_activeDeduplicationKeys.Contains(messageEvent.DeduplicationKey))
            {
                const string reason = "通知仍在通知中心显示，跳过重复内容";
                diagnostics.RecordIgnored(reason);
                return MessagePipelineResult.Ignore(reason, messageEvent);
            }

            if (_seen.TryGetValue(messageEvent.DeduplicationKey, out var seenAt)
                && now - seenAt < TimeSpan.FromSeconds(settings.CooldownSeconds))
            {
                var reason = $"去重冷却中: {settings.CooldownSeconds}s";
                diagnostics.RecordIgnored(reason);
                return MessagePipelineResult.Ignore(reason, messageEvent);
            }

            _seen[messageEvent.DeduplicationKey] = now;
            CleanupSeen(now);
        }

        var evaluation = ruleEvaluator.Evaluate(messageEvent);
        if (evaluation.IsIgnored)
        {
            diagnostics.RecordIgnored(evaluation.Reason);
            logger.LogInformation("消息已忽略: {Reason} Sender={Sender}", evaluation.Reason, evaluation.Event.Sender);
            return MessagePipelineResult.Ignore(evaluation.Reason, evaluation.Event);
        }

        diagnostics.RecordAccepted(evaluation.Event, evaluation.Reason);
        return MessagePipelineResult.Accept(evaluation.Event, evaluation.Reason);
    }

    public void UpdateActiveEvents(IEnumerable<MessageEvent> visibleEvents)
    {
        var keys = visibleEvents
            .Select(x => x.DeduplicationKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            _activeDeduplicationKeys = keys;
        }
    }

    public void ClearState()
    {
        lock (_lock)
        {
            _seen.Clear();
            _activeDeduplicationKeys.Clear();
        }
    }

    private void CleanupSeen(DateTimeOffset now)
    {
        foreach (var item in _seen.Where(x => now - x.Value > TimeSpan.FromMinutes(10)).ToArray())
        {
            _seen.Remove(item.Key);
        }
    }
}
