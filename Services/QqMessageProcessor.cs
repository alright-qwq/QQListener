using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public class QqMessageProcessor
{
    private readonly QqListenerSettings _settings;
    private readonly ILogger<QqMessageProcessor> _logger;
    private readonly Dictionary<int, DateTimeOffset> _seen = new();
    private HashSet<int> _activeToastKeys = [];
    private readonly object _lock = new();

    public QqMessageProcessor(QqListenerSettings settings, ILogger<QqMessageProcessor> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public QqNotificationMessage? Process(IReadOnlyList<string> texts)
    {
        var normalized = NormalizeTexts(texts);
        normalized = RemoveNoiseLines(normalized);
        if (normalized.Length == 0)
            return null;

        var key = Hash(normalized);
        var now = DateTimeOffset.Now;

        lock (_lock)
        {
            if (_activeToastKeys.Contains(key))
                return null;

            if (_seen.TryGetValue(key, out var seenAt)
                && now - seenAt < TimeSpan.FromSeconds(_settings.CooldownSeconds))
            {
                return null;
            }

            _seen[key] = now;
            CleanupSeen(now);
        }

        var sender = "QQ";
        var message = "";

        if (normalized.Length == 1)
        {
            SplitSingleLine(normalized[0], ref sender, ref message);
        }
        else
        {
            // Group notifications: ["GroupName", "Person：Content"] or ["GroupName", "Person", "Content"]
            // Try splitting the last element to extract sender from "Person：Content" format.
            var lastLine = normalized[^1];
            var splitDone = false;
            foreach (var separator in new[] { "\uff1a", ": " })
            {
                var idx = lastLine.IndexOf(separator, StringComparison.Ordinal);
                if (idx <= 0 || idx >= lastLine.Length - separator.Length) continue;
                sender = lastLine[..idx].Trim();
                message = lastLine[(idx + separator.Length)..].Trim();
                if (string.IsNullOrWhiteSpace(sender)) continue;
                splitDone = true;
                break;
            }

            if (!splitDone)
            {
                sender = normalized[0];
                message = string.Join(Environment.NewLine, normalized.Skip(1));
            }
            else if (normalized.Length > 2)
            {
                // Keep middle lines as part of the message (e.g. ["Group", "Person", "Content"])
                var middle = string.Join(Environment.NewLine, normalized[1..^1]);
                if (!string.IsNullOrWhiteSpace(middle))
                    message = middle + Environment.NewLine + message;
            }
        }
        var fullText = string.Join(Environment.NewLine, normalized);

        if (_settings.Blacklist.Any(x => fullText.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("通知命中黑名单 [{Sender}]，已忽略。", sender);
            return null;
        }

        var calling = _settings.CallingEnabled
                      && !string.IsNullOrWhiteSpace(_settings.CallingKeyword)
                      && fullText.Contains(_settings.CallingKeyword, StringComparison.OrdinalIgnoreCase);

        var important = calling
                        || _settings.ImportantPersons.Any(x => sender.Contains(x, StringComparison.OrdinalIgnoreCase))
                        || _settings.ImportantKeywords.Any(x => fullText.Contains(x, StringComparison.OrdinalIgnoreCase))
                        || (_settings.SomeoneAtMe && sender.Contains("有人@我", StringComparison.OrdinalIgnoreCase));

        var duration = calling
            ? TimeSpan.FromSeconds(_settings.CallingDurationSeconds)
            : TimeSpan.FromSeconds(important ? _settings.ImportantDurationSeconds : _settings.NormalDurationSeconds);

        if (calling)
            _logger.LogInformation("检测到呼叫关键词 \"{Keyword}\"", _settings.CallingKeyword);

        return new QqNotificationMessage(sender, message, important, calling, duration);
    }

    public void UpdateActiveToasts(IEnumerable<IReadOnlyList<string>> visibleToasts)
    {
        var newKeys = visibleToasts
            .Select(NormalizeTexts)
            .Where(x => x.Length > 0)
            .Select(Hash)
            .ToHashSet();
        lock (_lock)
        {
            _activeToastKeys = newKeys;
        }
    }

    public void ClearState()
    {
        lock (_lock)
        {
            _seen.Clear();
            _activeToastKeys.Clear();
        }
    }

    private static string[] NormalizeTexts(IEnumerable<string> texts)
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
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void SplitSingleLine(string text, ref string sender, ref string message)
    {
        // Handles "GroupName Person：Content" → sender=Person, message=Content
        // and "Person：Content" → sender=Person, message=Content
        // Use IndexOf (first occurrence) instead of LastIndexOf: when the message body
        // itself contains the separator (e.g. "Alice：Hello：World"), LastIndexOf would
        // incorrectly treat "Alice：Hello" as the sender. The first separator is the
        // boundary between sender and content.
        var separators = new[] { "\uff1a", ": " };
        foreach (var separator in separators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index >= text.Length - separator.Length) continue;
            var candidate = text[..index].Trim();
            var rest = text[(index + separator.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(rest)) continue;
            sender = candidate;
            message = rest;
            return;
        }
    }

    private static int Hash(IEnumerable<string> values)
    {
        var hash = new HashCode();
        foreach (var v in values)
        {
            hash.Add(v);
        }

        return hash.ToHashCode();
    }

    private void CleanupSeen(DateTimeOffset now)
    {
        foreach (var item in _seen.Where(x => now - x.Value > TimeSpan.FromMinutes(10)).ToArray())
        {
            _seen.Remove(item.Key);
        }
    }
}
