using ClassIsland.Core.Attributes;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Triggers;

[TriggerInfo(AutomationId, "收到重要 QQ 消息时", "\uE7C1")]
public sealed class ImportantMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Important)
{
    internal const string AutomationId = "QQListener.ImportantMessageReceived";
}
