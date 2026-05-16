using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.disable", "暂停 QQ 监听", "\uE716", addDefaultToMenu: false)]
public class DisableQqListenerAction(QqListenerSettings settings, ILogger<DisableQqListenerAction> logger) : ActionBase
{
    protected override async Task OnInvoke()
    {
        settings.IsEnabled = false;
        logger.LogInformation("自动化：QQ 监听已暂停。");
        await base.OnInvoke();
    }

    protected override async Task OnRevert()
    {
        settings.IsEnabled = true;
        logger.LogInformation("自动化：QQ 监听已回退为开启。");
        await base.OnRevert();
    }
}
