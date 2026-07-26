using QQListener.Models;

namespace QQListener.Services;

public class MessageRuleEvaluator(QqListenerSettings settings)
{
    public MessageRuleEvaluationResult Evaluate(MessageEvent messageEvent)
    {
        var fullText = BuildSearchText(messageEvent);

        var blacklist = settings.Blacklist.FirstOrDefault(x => Contains(fullText, x));
        if (!string.IsNullOrWhiteSpace(blacklist))
        {
            return Ignore(messageEvent, "命中黑名单规则");
        }

        var allowedSources = settings.AllowedSources;
        if (allowedSources.Count > 0
            && !allowedSources.Contains(messageEvent.Source, StringComparer.OrdinalIgnoreCase))
        {
            return Ignore(messageEvent, "来源不在允许列表");
        }

        if (settings.RuleTimeWindowEnabled && !IsInsideAllowedWindow(messageEvent.ReceivedAt.TimeOfDay))
        {
            return Ignore(messageEvent, "不在允许时间段");
        }

        if (settings.CallingEnabled
            && !string.IsNullOrWhiteSpace(settings.CallingKeyword)
            && Contains(fullText, settings.CallingKeyword))
        {
            return new MessageRuleEvaluationResult(
                messageEvent with { Importance = MessageImportance.Calling },
                false,
                "命中呼叫关键词");
        }

        var importantPerson = settings.ImportantPersons.FirstOrDefault(x => Contains(messageEvent.Sender, x));
        if (!string.IsNullOrWhiteSpace(importantPerson))
        {
            return new MessageRuleEvaluationResult(
                messageEvent with { Importance = MessageImportance.Important },
                false,
                "命中重要人物规则");
        }

        var importantKeyword = settings.ImportantKeywords.FirstOrDefault(x => Contains(fullText, x));
        if (!string.IsNullOrWhiteSpace(importantKeyword))
        {
            return new MessageRuleEvaluationResult(
                messageEvent with { Importance = MessageImportance.Important },
                false,
                "命中重要关键词");
        }

        if (settings.SomeoneAtMe && Contains(fullText, "有人@我"))
        {
            return new MessageRuleEvaluationResult(
                messageEvent with { Importance = MessageImportance.Important },
                false,
                "检测到有人@我");
        }

        return new MessageRuleEvaluationResult(
            messageEvent with { Importance = MessageImportance.Normal },
            false,
            "未命中特殊规则");
    }

    private static string BuildSearchText(MessageEvent messageEvent)
    {
        // 注意：不要把 SourceDisplayName（如 "QQ"）拼进搜索文本，否则用户把 "QQ" 加进
        // 黑名单/重要关键词时，每条消息都会被强行命中。
        return string.Join(Environment.NewLine, new[]
            {
                messageEvent.Sender,
                messageEvent.Conversation,
                messageEvent.Body,
                messageEvent.FullText
            }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool Contains(string value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value)
               && !string.IsNullOrWhiteSpace(keyword)
               && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInsideAllowedWindow(TimeSpan time)
    {
        var start = TimeOnly.ParseExact(
                settings.RuleTimeWindowStartText,
                "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture)
            .ToTimeSpan();
        var end = TimeOnly.ParseExact(
                settings.RuleTimeWindowEndText,
                "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture)
            .ToTimeSpan();

        if (start == end) return true;
        return start < end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    private static MessageRuleEvaluationResult Ignore(MessageEvent messageEvent, string reason) => new(
        messageEvent with { Importance = MessageImportance.Ignored },
        true,
        reason);
}
