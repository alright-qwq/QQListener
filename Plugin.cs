using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Actions;
using QQListener.Models;
using QQListener.Services;
using QQListener.Views.SettingsPages;

namespace QQListener;

[PluginEntrance]
public class Plugin : PluginBase
{
    [STAThread]
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var settings = QqListenerSettings.Load(PluginConfigFolder);
        settings.Normalize();
        services.AddSingleton(settings);
        services.AddSingleton<WindowsNotificationReader>();
        services.AddSingleton<QqMessageProcessor>();
        services.AddNotificationProvider<QqNotificationProvider>();
        services.AddSettingsPage<QqListenerSettingsPage>();

        BuildActionMenuTree();

        services.AddAction<EnableQqListenerAction>();
        services.AddAction<DisableQqListenerAction>();
        services.AddAction<ToggleQqListenerAction>();
    }

    private static void BuildActionMenuTree()
    {
        IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup("QQListener", "\uE715",
            new ActionMenuTreeItem("qqlistener.action.enable", "开启 QQ 监听", "\uE715"),
            new ActionMenuTreeItem("qqlistener.action.disable", "暂停 QQ 监听", "\uE716"),
            new ActionMenuTreeItem("qqlistener.action.toggle", "切换 QQ 监听", "\uE7B3")
        ));
    }
}
