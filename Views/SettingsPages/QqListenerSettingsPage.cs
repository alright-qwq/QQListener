using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "\uE02B", "\uE02A")]
public partial class QqListenerSettingsPage : SettingsPageBase
{
    private readonly QqListenerSettings _settings;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private DispatcherTimer? _saveTimer;

    public QqListenerSettings Settings => _settings;

    public QqListenerDiagnosticsState Diagnostics => _diagnostics;

    public QqListenerSettingsPage(QqListenerSettings settings, QqListenerDiagnosticsState diagnostics)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        DataContext = this;
        InitializeComponent();

        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DebounceSave();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_saveTimer != null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer = null;
        }
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
        if (_settings.SuppressAutoSave) return;
        _settings.Normalize();
        _settings.Save();
    }
}
