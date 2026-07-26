using ClassIsland.Core.Abstractions.Automation;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Automations.Triggers;

public abstract class MessageReceivedTriggerBase(
    MessageAutomationEventHub hub,
    MessageImportance importance) : TriggerBase
{
    private bool _loaded;

    public override void Loaded()
    {
        if (_loaded) return;

        _loaded = true;
        hub.MessageReceived += OnMessageReceived;
    }

    public override void UnLoaded()
    {
        if (!_loaded) return;

        hub.MessageReceived -= OnMessageReceived;
        _loaded = false;
    }

    protected virtual void RaiseTrigger() => Trigger();

    internal bool MatchesImportance(MessageImportance candidate) => candidate == importance;

    private void OnMessageReceived(object? sender, MessageAutomationEventArgs args)
    {
        if (MatchesImportance(args.Importance))
        {
            RaiseTrigger();
        }
    }
}
