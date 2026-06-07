using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Actions;

[ActionInfo("QQListener.ShowLatestMessage", "显示最后一条 QQ 消息", "\uE02B", addDefaultToMenu: false)]
public class ShowLatestMessageAction(
    LatestMessageState latestMessage) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var message = latestMessage.HasMessage
            ? new MessageEvent(
                "qq.toast",
                latestMessage.Source,
                latestMessage.Sender,
                latestMessage.Conversation,
                latestMessage.Message,
                latestMessage.ReceivedAt,
                latestMessage.Importance,
                [latestMessage.Sender, latestMessage.Message],
                $"{latestMessage.Sender}\u001f{latestMessage.Message}")
            : new MessageEvent(
                "qqlistener.system",
                "QQListener",
                "QQListener",
                "",
                "暂无 QQ 消息",
                DateTimeOffset.Now,
                MessageImportance.Normal,
                ["暂无 QQ 消息"],
                "qqlistener.system.empty");

        var notificationProvider = IAppHost.Host?.Services
            .GetServices<IHostedService>()
            .OfType<QqNotificationProvider>()
            .FirstOrDefault();
        notificationProvider?.ShowQqNotification(message);
    }
}
