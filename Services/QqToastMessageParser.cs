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
        out string reason)
    {
        var rawText = NormalizeTexts(texts);
        var contentLines = RemoveNoiseLines(rawText);
        if (contentLines.Length == 0)
        {
            messageEvent = null;
            reason = "通知没有可解析文本";
            return false;
        }

        var sender = SourceDisplayName;
        var conversation = "";
        var body = "";

        if (contentLines.Length == 1)
        {
            ParseSingleLine(contentLines[0], ref sender, ref conversation, ref body);
        }
        else
        {
            ParseMultiLine(contentLines, ref sender, ref conversation, ref body);
        }

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
        reason = "已解析 QQ Toast";
        return true;
    }

    public MessageEvent? ParseOrDefault(IReadOnlyList<string> texts, DateTimeOffset receivedAt)
    {
        return TryParse(texts, receivedAt, out var messageEvent, out _) ? messageEvent : null;
    }

    public static bool LooksLikeQqToastShape(IReadOnlyList<string> texts)
    {
        var lines = RemoveNoiseLines(NormalizeTexts(texts));
        if (lines.Length == 0) return false;
        if (HasQqTextMarker(lines)) return true;
        if (lines.Length >= 2) return true;

        return TrySplitSenderAndBody(lines[0], out _, out _);
    }

    private static void ParseSingleLine(string text, ref string sender, ref string conversation, ref string body)
    {
        if (TrySplitSenderAndBody(text, out var candidateSender, out var candidateBody))
        {
            SplitConversationPrefix(candidateSender, ref sender, ref conversation);
            body = candidateBody;
            return;
        }

        body = text;
    }

    private static void ParseMultiLine(string[] lines, ref string sender, ref string conversation, ref string body)
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

            return;
        }

        if (lines.Length >= 3)
        {
            conversation = lines[0];
            sender = lines[1];
            body = string.Join(Environment.NewLine, lines[2..]);
            return;
        }

        sender = lines[0];
        body = string.Join(Environment.NewLine, lines.Skip(1));
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

    private static bool HasQqTextMarker(IEnumerable<string> lines)
    {
        var fullText = string.Join('\n', lines);
        return fullText.Contains("QQ", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("TIM", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("有人@我", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("群聊", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("[图片]", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("[表情]", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("[语音]", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateDeduplicationKey(string source, IEnumerable<string> values)
    {
        var joined = string.Join('\u001f', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}\u001e{joined}"));
        return $"{source}:{Convert.ToHexString(bytes)}";
    }
}
