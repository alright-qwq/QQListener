using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Automations.Actions;
using QQListener.Components;
using QQListener.Models;
using QQListener.Services;
using QQListener.Views.ActionSettingsControls;
using QQListener.Views.SettingsPages;

namespace QQListener;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var settings = QqListenerSettings.Load(PluginConfigFolder);
        settings.Normalize();
        services.AddSingleton(settings);
        services.AddSingleton<WindowsNotificationReader>();
        services.AddSingleton<QqMessageProcessor>();
        services.AddSingleton<LatestMessageState>();

        services.AddNotificationProvider<QqNotificationProvider>();
        services.AddComponent<LatestQqMessageComponent, LatestQqMessageComponentSettingsControl>();
        RegisterBaseActions(services);
        RegisterActionMenu();

        services.AddSettingsPage<QqListenerSettingsPage>();
    }

    private static void RegisterBaseActions(IServiceCollection services)
    {
        services.AddAction<SetListenerEnabledAction, SetListenerEnabledActionSettingsControl>();
        services.AddAction<ShowLatestMessageAction>();
    }

    private static void RegisterActionMenu()
    {
        const string groupName = "QQListener 行动";
        if (!IActionService.ActionMenuTree.Contains(groupName))
        {
            IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup(groupName, "\uE02B"));
        }

        var children = IActionService.ActionMenuTree[groupName];
        if (!children.Contains("设置监听状态"))
        {
            children.Add(new ActionMenuTreeItem<SetListenerEnabledActionSettings>(
                "QQListener.SetListenerEnabled",
                "设置监听状态",
                "\uE73C",
                _ => { }));
        }

        if (!children.Contains("显示最后一条 QQ 消息"))
        {
            children.Add(new ActionMenuTreeItem(
                "QQListener.ShowLatestMessage",
                "显示最后一条 QQ 消息",
                "\uE02B"));
        }
    }
}
