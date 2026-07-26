using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Automations.Actions;

[ActionInfo("QQListener.ClearLatestMessage", "清空最后一条 QQ 消息", "\uE74D", addDefaultToMenu: false)]
public class ClearLatestMessageAction(LatestMessageState latestMessage) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        latestMessage.Clear();
    }
}
