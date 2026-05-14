using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public class QqMessageProcessor
{
    private readonly QqListenerSettings _settings;
    private readonly ILogger<QqMessageProcessor> _logger;
    private readonly Dictionary<int, DateTimeOffset> _seen = new();
    private HashSet<int> _activeToastKeys = [];

    public QqMessageProcessor(QqListenerSettings settings, ILogger<QqMessageProcessor> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public QqNotificationMessage? Process(IReadOnlyList<string> texts)
    {
        var normalized = NormalizeTexts(texts);
        if (normalized.Length < 2)
            return null;

        var key = Hash(normalized);
        var now = DateTimeOffset.Now;

        if (_activeToastKeys.Contains(key))
            return null;

        if (_seen.TryGetValue(key, out var seenAt)
            && now - seenAt < TimeSpan.FromSeconds(_settings.CooldownSeconds))
        {
            return null;
        }

        _seen[key] = now;
        CleanupSeen(now);

        var sender = normalized[0];
        var message = string.Join(Environment.NewLine, normalized.Skip(1));
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
        _activeToastKeys = visibleToasts
            .Select(NormalizeTexts)
            .Where(x => x.Length > 0)
            .Select(Hash)
            .ToHashSet();
    }

    private static string[] NormalizeTexts(IEnumerable<string> texts)
    {
        return texts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => string.Join(' ', x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
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
