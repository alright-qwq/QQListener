namespace QQListener.Models;

public sealed record MessageRulePreviewResult(
    bool Success,
    MessageImportance Importance,
    bool IsIgnored,
    string Reason);
