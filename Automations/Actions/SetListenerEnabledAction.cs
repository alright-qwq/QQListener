using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Automations.Actions;

[ActionInfo(AutomationId, "设置 QQListener 监听状态", "\uE73C", addDefaultToMenu: false)]
public class SetListenerEnabledAction(QqListenerSettings settings) : ActionBase<SetListenerEnabledActionSettings>
{
    internal const string AutomationId = "QQListener.SetListenerEnabled";

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        settings.ModifyAndSave(x => x.IsEnabled = Settings.IsEnabled);
    }
}
