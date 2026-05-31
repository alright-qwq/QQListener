using Avalonia;
using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using QQListener.Automations.Actions;

namespace QQListener.Views.ActionSettingsControls;

public class SetListenerEnabledActionSettingsControl : ActionSettingsControlBase<SetListenerEnabledActionSettings>
{
    private readonly ToggleSwitch _toggleSwitch;
    private bool _isUpdatingFromSettings;

    public SetListenerEnabledActionSettingsControl()
    {
        _toggleSwitch = new ToggleSwitch
        {
            OnContent = "启用监听",
            OffContent = "禁用监听"
        };
        _toggleSwitch.PropertyChanged += ToggleSwitchOnPropertyChanged;

        Content = new StackPanel
        {
            Spacing = 8,
            Margin = new(10),
            Children =
            {
                new TextBlock { Text = "QQListener 监听状态" },
                _toggleSwitch
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncFromSettings();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.IsEnabled))
        {
            SyncFromSettings();
        }
    }

    private void ToggleSwitchOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_isUpdatingFromSettings || e.Property != ToggleSwitch.IsCheckedProperty)
        {
            return;
        }

        if (_toggleSwitch.IsChecked is bool isChecked)
        {
            Settings.IsEnabled = isChecked;
        }
    }

    private void SyncFromSettings()
    {
        _isUpdatingFromSettings = true;
        try
        {
            _toggleSwitch.IsChecked = Settings.IsEnabled;
        }
        finally
        {
            _isUpdatingFromSettings = false;
        }
    }
}
