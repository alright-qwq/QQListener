using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Automations.Actions;

public partial class SetListenerEnabledActionSettings : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;
}
