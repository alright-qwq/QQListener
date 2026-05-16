using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.enable", "开启 QQ 监听", "\uE715", addDefaultToMenu: false)]
public class EnableQqListenerAction(QqListenerSettings settings, ILogger<EnableQqListenerAction> logger) : ActionBase
{
    protected override async Task OnInvoke()
    {
        settings.IsEnabled = true;
        logger.LogInformation("自动化：QQ 监听已开启。");
        await base.OnInvoke();
    }

    protected override async Task OnRevert()
    {
        settings.IsEnabled = false;
        logger.LogInformation("自动化：QQ 监听已回退为暂停。");
        await base.OnRevert();
    }
}
