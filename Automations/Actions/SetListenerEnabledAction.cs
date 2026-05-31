using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Automations.Actions;

[ActionInfo("QQListener.SetListenerEnabled", "设置 QQListener 监听状态", "\uE73C", addDefaultToMenu: false)]
public class SetListenerEnabledAction(QqListenerSettings settings) : ActionBase<SetListenerEnabledActionSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        settings.ModifyAndSave(x => x.IsEnabled = Settings.IsEnabled);
    }
}
