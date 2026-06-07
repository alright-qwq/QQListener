using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Actions;

[ActionInfo("QQListener.ShowLatestMessage", "显示最后一条 QQ 消息", "", addDefaultToMenu: false)]
public class ShowLatestMessageAction(LatestMessageState latestMessage) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        if (!latestMessage.HasMessage)
        {
            // 没有最近消息时静默：用户主动触发"显示最后一条"却看到"暂无消息"提醒
            // 反而像 bug，不如不打扰。诊断面板里仍能看到状态。
            return;
        }

        var message = new MessageEvent(
            "qq.toast",
            string.IsNullOrWhiteSpace(latestMessage.Source) ? "QQ" : latestMessage.Source,
            latestMessage.Sender,
            latestMessage.Conversation,
            latestMessage.Message,
            latestMessage.ReceivedAt,
            latestMessage.Importance,
            [latestMessage.Sender, latestMessage.Message],
            $"qq.toast:replay:{latestMessage.Sender}");

        // QqNotificationProvider 通过 AddNotificationProvider 同时注册为 IHostedService，
        // 但它没有暴露在 DI 里作为可直接注入的服务。先尝试 GetService<>()，再退而
        // 求其次从 hosted-service 列表里找。
        var host = IAppHost.Host?.Services;
        if (host == null) return;

        var provider = host.GetService<QqNotificationProvider>()
                       ?? host.GetServices<IHostedService>().OfType<QqNotificationProvider>().FirstOrDefault();
        provider?.ShowQqNotification(message);
    }
}
