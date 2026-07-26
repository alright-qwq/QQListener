using System.Globalization;
using QQListener.Models;

namespace QQListener.Services;

public sealed class MessageRulePreviewService(
    QqToastMessageParser parser,
    MessageRuleEvaluator evaluator,
    TimeProvider timeProvider)
{
    private const string DefaultSource = "qq.toast";

    public MessageRulePreviewResult Preview(string toastText, string source, string? timeText)
    {
        if (!TryResolvePreviewTime(timeText, out var previewTime))
        {
            return new MessageRulePreviewResult(false, MessageImportance.Normal, false, "时间格式必须为 HH:mm");
        }

        var lines = (toastText ?? "").Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        if (!parser.TryParse(lines, previewTime, out var messageEvent, out var reason))
        {
            return new MessageRulePreviewResult(false, MessageImportance.Normal, false, reason);
        }

        var previewSource = string.IsNullOrWhiteSpace(source) ? DefaultSource : source;
        var evaluated = evaluator.Evaluate(messageEvent! with
        {
            Source = previewSource,
            ReceivedAt = previewTime
        });
        return new MessageRulePreviewResult(true, evaluated.Event.Importance, evaluated.IsIgnored, evaluated.Reason);
    }

    private bool TryResolvePreviewTime(string? timeText, out DateTimeOffset previewTime)
    {
        var localNow = timeProvider.GetLocalNow();
        if (string.IsNullOrWhiteSpace(timeText))
        {
            previewTime = localNow;
            return true;
        }

        if (!TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            previewTime = default;
            return false;
        }

        previewTime = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, time.Hour, time.Minute, 0, localNow.Offset);
        return true;
    }
}
