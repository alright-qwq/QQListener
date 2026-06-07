namespace QQListener.Models;

public sealed record MessageRuleEvaluationResult(
    MessageEvent Event,
    bool IsIgnored,
    string Reason);

public sealed record MessagePipelineResult(
    MessageEvent? Event,
    bool Accepted,
    string Reason)
{
    public static MessagePipelineResult Accept(MessageEvent messageEvent, string reason) =>
        new(messageEvent, true, reason);

    public static MessagePipelineResult Ignore(string reason, MessageEvent? messageEvent = null) =>
        new(messageEvent, false, reason);
}
