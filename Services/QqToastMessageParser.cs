using System.Security.Cryptography;
using System.Text;
using QQListener.Models;

namespace QQListener.Services;

public class QqToastMessageParser
{
    private const string SourceId = "qq.toast";
    private const string SourceDisplayName = "QQ";

    public bool TryParse(
        IReadOnlyList<string> texts,
        DateTimeOffset receivedAt,
        out MessageEvent? messageEvent,
        out string reason) =>
        TryParse(texts, receivedAt, out messageEvent, out reason, out _);

    public bool TryParse(
        IReadOnlyList<string> texts,
        DateTimeOffset receivedAt,
        out MessageEvent? messageEvent,
        out string reason,
        out QqToastParseConfidence confidence)
    {
        var rawText = NormalizeTexts(texts);
        var contentLines = RemoveNoiseLines(rawText);
        confidence = QqToastParseConfidence.Degraded;
        if (contentLines.Length == 0)
        {
            messageEvent = null;
            reason = "通知没有可解析文本";
            return false;
        }

        var sender = SourceDisplayName;
        var conversation = "";
        var body = "";
        var known = contentLines.Length == 1
            ? ParseSingleLine(contentLines[0], ref sender, ref conversation, ref body)
            : ParseMultiLine(contentLines, ref sender, ref conversation, ref body);
        confidence = known ? QqToastParseConfidence.Known : QqToastParseConfidence.Degraded;

        messageEvent = new MessageEvent(
            SourceId,
            SourceDisplayName,
            sender,
            conversation,
            body,
            receivedAt,
            MessageImportance.Normal,
            rawText,
            CreateDeduplicationKey(SourceId, contentLines));
        reason = known ? "已解析 QQ Toast" : "未知格式，已降级解析";
        return true;
    }

    public MessageEvent? ParseOrDefault(IReadOnlyList<string> texts, DateTimeOffset receivedAt)
    {
        return TryParse(texts, receivedAt, out var messageEvent, out _) ? messageEvent : null;
    }

    private static bool ParseSingleLine(string text, ref string sender, ref string conversation, ref string body)
    {
        if (TrySplitSenderAndBody(text, out var candidateSender, out var candidateBody))
        {
            SplitConversationPrefix(candidateSender, ref sender, ref conversation);
            body = candidateBody;
            return true;
        }

        body = text;
        return false;
    }

    private static bool ParseMultiLine(string[] lines, ref string sender, ref string conversation, ref string body)
    {
        var lastLine = lines[^1];
        if (TrySplitSenderAndBody(lastLine, out var candidateSender, out var candidateBody))
        {
            conversation = lines[0];
            sender = candidateSender;

            if (lines.Length > 2)
            {
                var middle = string.Join(Environment.NewLine, lines[1..^1]);
                body = string.IsNullOrWhiteSpace(middle)
                    ? candidateBody
                    : middle + Environment.NewLine + candidateBody;
            }
            else
            {
                body = candidateBody;
            }

            return true;
        }

        if (lines.Length >= 3)
        {
            conversation = lines[0];
            sender = lines[1];
            body = string.Join(Environment.NewLine, lines[2..]);
            return true;
        }

        sender = lines[0];
        body = string.Join(Environment.NewLine, lines.Skip(1));
        return false;
    }

    private static bool TrySplitSenderAndBody(string text, out string sender, out string body)
    {
        foreach (var separator in new[] { "\uff1a", ": ", ":" })
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index >= text.Length - separator.Length) continue;

            sender = text[..index].Trim();
            body = text[(index + separator.Length)..].Trim();
            return !string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(body);
        }

        sender = "";
        body = "";
        return false;
    }

    private static void SplitConversationPrefix(string candidateSender, ref string sender, ref string conversation)
    {
        var lastSpace = candidateSender.LastIndexOf(' ');
        if (lastSpace <= 0 || lastSpace >= candidateSender.Length - 1)
        {
            sender = candidateSender;
            return;
        }

        conversation = candidateSender[..lastSpace].Trim();
        sender = candidateSender[(lastSpace + 1)..].Trim();
    }

    public static string[] NormalizeTexts(IEnumerable<string> texts)
    {
        return texts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => string.Join(' ', x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
    }

    private static string[] RemoveNoiseLines(string[] values)
    {
        return values
            .Where(x => !x.Equals("QQ", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Equals("TIM", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string CreateDeduplicationKey(string source, IEnumerable<string> values)
    {
        var joined = string.Join('\u001f', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}\u001e{joined}"));
        return $"{source}:{Convert.ToHexString(bytes)}";
    }
}
