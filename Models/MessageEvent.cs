namespace QQListener.Models;

public sealed record MessageEvent(
    string Source,
    string SourceDisplayName,
    string Sender,
    string Conversation,
    string Body,
    DateTimeOffset ReceivedAt,
    MessageImportance Importance,
    IReadOnlyList<string> RawText,
    string DeduplicationKey)
{
    public bool IsIgnored => Importance == MessageImportance.Ignored;

    public bool IsImportant => Importance is MessageImportance.Important or MessageImportance.Calling;

    public bool IsCalling => Importance == MessageImportance.Calling;

    public string FullText => string.Join(Environment.NewLine, RawText);

    public string DisplayTitle => IsCalling
        ? $"{SourceDisplayName} 呼叫"
        : IsImportant
            ? $"重要 {SourceDisplayName} 消息"
            : $"{SourceDisplayName} 消息";

    public string DisplayBody => string.IsNullOrWhiteSpace(Body)
        ? Sender
        : $"{Sender}{Environment.NewLine}{Body}";
}
