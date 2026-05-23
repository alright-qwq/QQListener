using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "\uE713", "\uE715")]
public partial class QqListenerSettingsPage : SettingsPageBase, INotifyPropertyChanged
{
    private readonly QqListenerSettings _settings;
    private DispatcherTimer? _saveTimer;
    private bool _isFeatureDrawerOpen;
    private object? _featureDrawerContent;

    public QqListenerSettings Settings => _settings;

    public ObservableCollection<ActionFeatureItem> ActionFeatureItems { get; } =
    [
        new ActionFeatureItem { Id = "qqlistener.action.enable",  DisplayName = "开启 QQ 监听", IsEnabled = true },
        new ActionFeatureItem { Id = "qqlistener.action.disable", DisplayName = "暂停 QQ 监听", IsEnabled = true },
        new ActionFeatureItem { Id = "qqlistener.action.toggle",  DisplayName = "切换 QQ 监听", IsEnabled = true },
    ];

    public bool IsFeatureDrawerOpen
    {
        get => _isFeatureDrawerOpen;
        set
        {
            _isFeatureDrawerOpen = value;
            OnPropertyChanged();
        }
    }

    public object? FeatureDrawerContent
    {
        get => _featureDrawerContent;
        set
        {
            _featureDrawerContent = value;
            OnPropertyChanged();
        }
    }

    public QqListenerSettingsPage(QqListenerSettings settings)
    {
        _settings = settings;
        DataContext = this;
        InitializeComponent();

        _settings.PropertyChanged += (_, _) => DebounceSave();
    }

    private void OnManageFeaturesClick(object? sender, RoutedEventArgs e)
    {
        FeatureDrawerContent = new object();
        IsFeatureDrawerOpen = true;
    }

    private void OnCloseDrawerClick(object? sender, RoutedEventArgs e)
    {
        IsFeatureDrawerOpen = false;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
