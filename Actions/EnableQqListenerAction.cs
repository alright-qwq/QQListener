using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.enable", "开启 QQ 监听", "\uE715")]
public class EnableQqListenerAction(ILogger<EnableQqListenerAction> logger) : ActionBase
{
    private readonly ILogger<EnableQqListenerAction> _logger = logger;
    private readonly QqListenerSettings _settings = IAppHost.GetService<QqListenerSettings>();

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = true; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已开启。");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = false; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已回退为暂停。");
    }
}
