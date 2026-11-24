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

    /// <summary>
    /// 通知正文：合并为单行显示，发送者与正文之间用全角冒号分隔。
    /// </summary>
    public string DisplayBody
    {
        get
        {
            var sender = CollapseToSingleLine(DisplaySender);
            var body = CollapseToSingleLine(Body);
            if (string.IsNullOrWhiteSpace(body)) return sender;
            if (string.IsNullOrWhiteSpace(sender)) return body;
            return $"{sender}：{body}";
        }
    }

    private static string CollapseToSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(" ", value.Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
