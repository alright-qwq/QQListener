using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Actions;

[ActionInfo(AutomationId, "显示最后一条 QQ 消息", "", addDefaultToMenu: false)]
public class ShowLatestMessageAction(QqListenerSettings settings) : ActionBase
{
    internal const string AutomationId = "QQListener.ShowLatestMessage";

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        if (!settings.IsEnabled) return;

        // QqNotificationProvider 通过 AddNotificationProvider 同时注册为 IHostedService，
        // 但它没有暴露在 DI 里作为可直接注入的服务。先尝试 GetService<>()，再退而
        // 求其次从 hosted-service 列表里找。
        var host = IAppHost.Host?.Services;
        if (host == null) return;

        var provider = host.GetService<QqNotificationProvider>()
                       ?? host.GetServices<IHostedService>().OfType<QqNotificationProvider>().FirstOrDefault();
        if (provider is null || !settings.IsEnabled) return;

        await provider.ReplayLatestAsync();
    }
}
