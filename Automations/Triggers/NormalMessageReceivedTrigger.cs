using ClassIsland.Core.Attributes;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Triggers;

[TriggerInfo(AutomationId, "收到普通 QQ 消息时", "\uE02B")]
public sealed class NormalMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Normal)
{
    internal const string AutomationId = "QQListener.NormalMessageReceived";
}
