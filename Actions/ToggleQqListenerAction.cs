using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.toggle", "切换 QQ 监听", "\uE7B3", addDefaultToMenu: false)]
public class ToggleQqListenerAction(QqListenerSettings settings, ILogger<ToggleQqListenerAction> logger) : ActionBase
{
    private bool _previousState;

    protected override async Task OnInvoke()
    {
        _previousState = settings.IsEnabled;
        settings.IsEnabled = !settings.IsEnabled;
        logger.LogInformation("自动化：QQ 监听已{State}", settings.IsEnabled ? "开启" : "暂停");
        await base.OnInvoke();
    }

    protected override async Task OnRevert()
    {
        settings.IsEnabled = _previousState;
        logger.LogInformation("自动化：QQ 监听已回退为{State}", _previousState ? "开启" : "暂停");
        await base.OnRevert();
    }
}
