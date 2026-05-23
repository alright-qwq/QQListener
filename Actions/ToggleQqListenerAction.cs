using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.toggle", "切换 QQ 监听", "\uE7B3")]
public class ToggleQqListenerAction(ILogger<ToggleQqListenerAction> logger) : ActionBase
{
    private readonly ILogger<ToggleQqListenerAction> _logger = logger;
    private readonly QqListenerSettings _settings = IAppHost.GetService<QqListenerSettings>();
    private bool _previousState;

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        _previousState = _settings.IsEnabled;
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = !_settings.IsEnabled; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已{State}", _settings.IsEnabled ? "开启" : "暂停");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = _previousState; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已回退为{State}", _previousState ? "开启" : "暂停");
    }
}
