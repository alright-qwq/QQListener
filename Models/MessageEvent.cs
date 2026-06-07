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

    public string DisplaySender
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Conversation)) return Sender;
            if (string.IsNullOrWhiteSpace(Sender)) return Conversation;
            // 解析器在某些两行 Toast 上会把同一段文本同时当作 Conversation 和 Sender，
            // 这里直接去重，避免显示成 "X / X"。
            return string.Equals(Conversation, Sender, StringComparison.Ordinal)
                ? Sender
                : $"{Conversation} / {Sender}";
        }
    }

    public string DisplayBody
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Body)) return DisplaySender;
            return string.IsNullOrWhiteSpace(DisplaySender)
                ? Body
                : $"{DisplaySender}{Environment.NewLine}{Body}";
        }
    }
}
