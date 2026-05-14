using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
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

        services.AddAction<EnableQqListenerAction>();
        services.AddAction<DisableQqListenerAction>();
        services.AddAction<ToggleQqListenerAction>();
    }
}
