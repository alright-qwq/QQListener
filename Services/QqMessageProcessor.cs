using System.Security.Cryptography;
using System.Text;
using QQListener.Models;

namespace QQListener.Services;

public class QqMessageProcessor(QqListenerSettings settings)
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new();
    private HashSet<string> _activeToastKeys = [];

    public QqNotificationMessage? Process(IReadOnlyList<string> texts)
    {
        var normalized = NormalizeTexts(texts);
        if (normalized.Length < 2)
        {
            return null;
        }

        var key = Hash(normalized);
        var now = DateTimeOffset.Now;
        if (_activeToastKeys.Contains(key))
        {
            return null;
        }

        if (_seen.TryGetValue(key, out var seenAt) && now - seenAt < TimeSpan.FromSeconds(settings.CooldownSeconds))
        {
            return null;
        }

        _seen[key] = now;
        CleanupSeen(now);

        var sender = normalized[0];
        var message = string.Join(Environment.NewLine, normalized.Skip(1));
        var fullText = string.Join(Environment.NewLine, normalized);

        if (settings.Blacklist.Any(x => fullText.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var calling = settings.CallingEnabled
                      && !string.IsNullOrWhiteSpace(settings.CallingKeyword)
                      && fullText.Contains(settings.CallingKeyword, StringComparison.OrdinalIgnoreCase);

        var important = calling
                        || settings.ImportantPersons.Any(x => sender.Contains(x, StringComparison.OrdinalIgnoreCase))
                        || settings.ImportantKeywords.Any(x => fullText.Contains(x, StringComparison.OrdinalIgnoreCase))
                        || (settings.SomeoneAtMe && sender.Contains("有人@我", StringComparison.OrdinalIgnoreCase));

        var duration = calling
            ? TimeSpan.FromSeconds(settings.CallingDurationSeconds)
            : TimeSpan.FromSeconds(important ? settings.ImportantDurationSeconds : settings.NormalDurationSeconds);

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

    private static string Hash(IEnumerable<string> values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', values)));
        return Convert.ToHexString(bytes);
    }

    private void CleanupSeen(DateTimeOffset now)
    {
        foreach (var item in _seen.Where(x => now - x.Value > TimeSpan.FromMinutes(10)).ToArray())
        {
            _seen.Remove(item.Key);
        }
    }
}
