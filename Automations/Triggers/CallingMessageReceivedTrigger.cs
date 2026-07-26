using ClassIsland.Core.Attributes;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Triggers;

[TriggerInfo(AutomationId, "收到 QQ 呼叫时", "\uE02A")]
public sealed class CallingMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Calling)
{
    internal const string AutomationId = "QQListener.CallingMessageReceived";
}
