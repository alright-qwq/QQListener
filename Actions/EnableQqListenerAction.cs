using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("QQListener.Enable", "开启 QQ 监听", "\uE715")]
public class EnableQqListenerAction : ActionBase
{
    private readonly QqListenerSettings _settings;
    private readonly ILogger<EnableQqListenerAction> _logger;

    public EnableQqListenerAction(QqListenerSettings settings, ILogger<EnableQqListenerAction> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _settings.IsEnabled = true;
        _logger.LogInformation("自动化：QQ 监听已开启。");
        await base.OnInvoke();
    }
}
