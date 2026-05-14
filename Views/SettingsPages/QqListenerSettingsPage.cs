using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "\uE713", "\uE715")]
public partial class QqListenerSettingsPage : SettingsPageBase
{
    private readonly QqListenerSettings _settings;
    private DispatcherTimer? _saveTimer;

    public QqListenerSettings Settings => _settings;

    public QqListenerSettingsPage(QqListenerSettings settings)
    {
        _settings = settings;
        DataContext = this;
        InitializeComponent();

        _settings.PropertyChanged += (_, _) => DebounceSave();
    }

    private void DebounceSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += OnSaveTimerTick;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        _settings.Normalize();
        _settings.Save();
    }
}
