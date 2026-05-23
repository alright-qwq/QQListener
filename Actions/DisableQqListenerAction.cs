using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Actions;

[ActionInfo("qqlistener.action.disable", "暂停 QQ 监听", "\uE716")]
public class DisableQqListenerAction(ILogger<DisableQqListenerAction> logger) : ActionBase
{
    private readonly ILogger<DisableQqListenerAction> _logger = logger;
    private readonly QqListenerSettings _settings = IAppHost.GetService<QqListenerSettings>();

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = false; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已暂停。");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();
        _settings.SuppressAutoSave = true;
        try { _settings.IsEnabled = true; }
        finally { _settings.SuppressAutoSave = false; }
        _logger.LogInformation("自动化：QQ 监听已回退为开启。");
    }
}
