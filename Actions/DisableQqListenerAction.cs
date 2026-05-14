using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("QQListener.Disable", "暂停 QQ 监听", "\uE716")]
public class DisableQqListenerAction : ActionBase
{
    private readonly QqListenerSettings _settings;
    private readonly ILogger<DisableQqListenerAction> _logger;

    public DisableQqListenerAction(QqListenerSettings settings, ILogger<DisableQqListenerAction> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _settings.IsEnabled = false;
        _logger.LogInformation("自动化：QQ 监听已暂停。");
        await base.OnInvoke();
    }
}
