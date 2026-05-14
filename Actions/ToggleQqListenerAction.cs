using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("QQListener.Toggle", "切换 QQ 监听", "\uE7B3")]
public class ToggleQqListenerAction : ActionBase
{
    private readonly QqListenerSettings _settings;
    private readonly ILogger<ToggleQqListenerAction> _logger;

    public ToggleQqListenerAction(QqListenerSettings settings, ILogger<ToggleQqListenerAction> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _settings.IsEnabled = !_settings.IsEnabled;
        _logger.LogInformation("自动化：QQ 监听已{State}", _settings.IsEnabled ? "开启" : "暂停");
        await base.OnInvoke();
    }
}
