using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Automations.Actions;

[ActionInfo(AutomationId, "清空最后一条 QQ 消息", "\uE74D", addDefaultToMenu: false)]
public class ClearLatestMessageAction(LatestMessageState latestMessage) : ActionBase
{
    internal const string AutomationId = "QQListener.ClearLatestMessage";

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        latestMessage.Clear();
    }
}
