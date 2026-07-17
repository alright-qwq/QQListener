namespace QQListener.Services;

public static class QqNotificationCandidateFilter
{
    private static readonly HashSet<string> TrustedDisplayNames =
        new(StringComparer.OrdinalIgnoreCase) { "QQ", "腾讯QQ", "TIM" };

    private static readonly string[] TrustedAppIdMarkers =
        ["tencentqq", "qqnt", "comtencentqq", "tencenttim", "comtencenttim"];

    public static bool IsCandidate(WindowsToastSnapshot snapshot, bool qqOnly)
    {
        if (!snapshot.IsAppIdentityAvailable) return false;

        var appName = snapshot.AppName.Trim();
        var appId = snapshot.AppId.Trim();
        if (string.IsNullOrWhiteSpace(appName) && string.IsNullOrWhiteSpace(appId)) return false;
        if (!qqOnly) return true;
        if (TrustedDisplayNames.Contains(appName)) return true;

        var normalizedAppId = new string(appId
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return TrustedAppIdMarkers.Any(normalizedAppId.Contains);
    }
}
