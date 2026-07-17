using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public class MessageEventPipeline(
    QqListenerSettings settings,
    MessageRuleEvaluator ruleEvaluator,
    QqListenerDiagnosticsState diagnostics,
    ILogger<MessageEventPipeline> logger,
    TimeProvider timeProvider)
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private HashSet<string> _activeDeduplicationKeys = [];
    private readonly object _lock = new();

    public MessagePipelineResult Process(MessageEvent messageEvent)
    {
        var now = timeProvider.GetUtcNow();
        var cooldown = TimeSpan.FromSeconds(Math.Clamp(settings.CooldownSeconds, 1, 3600));

        lock (_lock)
        {
            if (_activeDeduplicationKeys.Contains(messageEvent.DeduplicationKey))
            {
                const string reason = "通知仍在通知中心显示，跳过重复内容";
                diagnostics.RecordIgnored(reason);
                return MessagePipelineResult.Ignore(reason, messageEvent);
            }

            if (_seen.TryGetValue(messageEvent.DeduplicationKey, out var seenAt)
                && now - seenAt < cooldown)
            {
                var reason = $"去重冷却中: {settings.CooldownSeconds}s";
                diagnostics.RecordIgnored(reason);
                return MessagePipelineResult.Ignore(reason, messageEvent);
            }
        }

        var evaluation = ruleEvaluator.Evaluate(messageEvent);
        if (evaluation.IsIgnored)
        {
            // 不写 _seen：让规则发生变化（如用户移除黑名单项）后同 key 的合法消息能立刻命中。
            diagnostics.RecordIgnored(evaluation.Reason);
            logger.LogInformation(
                "消息已忽略: {Reason} Importance={Importance}",
                evaluation.Reason,
                evaluation.Event.Importance);
            return MessagePipelineResult.Ignore(evaluation.Reason, evaluation.Event);
        }

        lock (_lock)
        {
            _seen[messageEvent.DeduplicationKey] = now;
            CleanupSeen(now, cooldown);
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

    private void CleanupSeen(DateTimeOffset now, TimeSpan cooldown)
    {
        foreach (var item in _seen.Where(x => now - x.Value >= cooldown).ToArray())
        {
            _seen.Remove(item.Key);
        }
    }
}
