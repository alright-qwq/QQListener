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
    LatestMessageState latestMessage,
    QqListenerSettings settings) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var message = latestMessage.HasMessage
            ? new QqNotificationMessage(
                latestMessage.Sender,
                latestMessage.Message,
                latestMessage.IsImportant,
                latestMessage.IsCalling,
                TimeSpan.FromSeconds(latestMessage.IsCalling
                    ? settings.CallingDurationSeconds
                    : latestMessage.IsImportant
                        ? settings.ImportantDurationSeconds
                        : settings.NormalDurationSeconds))
            : new QqNotificationMessage(
                "QQListener",
                "暂无 QQ 消息",
                false,
                false,
                TimeSpan.FromSeconds(settings.NormalDurationSeconds));

        var notificationProvider = IAppHost.Host?.Services
            .GetServices<IHostedService>()
            .OfType<QqNotificationProvider>()
            .FirstOrDefault();
        notificationProvider?.ShowQqNotification(message);
    }
}
