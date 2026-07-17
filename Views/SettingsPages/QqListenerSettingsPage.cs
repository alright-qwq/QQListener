using System.ComponentModel;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "", "")]
public partial class QqListenerSettingsPage : SettingsPageBase, INotifyPropertyChanged
{
    private readonly QqListenerSettings _settings;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly QqListenerPluginInfo _pluginInfo;
    private readonly bool _autoSaveEnabled;
    private readonly QqListenerSettingsPersistenceCoordinator? _persistenceCoordinator;
    private DispatcherTimer? _saveTimer;
    private bool _providerAvailableCache;
    private bool _providerProbed;

    public QqListenerSettings Settings => _settings;
    public QqListenerDiagnosticsState Diagnostics => _diagnostics;
    public QqListenerPluginInfo PluginInfo => _pluginInfo;

    /// <summary>是否能找到 QqNotificationProvider 实例，决定快捷按钮是否可点。</summary>
    public bool ProviderAvailable
    {
        get
        {
            if (!_providerProbed)
            {
                _providerProbed = true;
                _providerAvailableCache = TryGetProvider() != null;
            }
            return _providerAvailableCache;
        }
    }

    public bool CanPoll => ProviderAvailable && _settings.IsEnabled;
    public bool CanReplay => ProviderAvailable && _settings.IsEnabled;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public QqListenerSettingsPage()
        : this(new QqListenerSettings(), new QqListenerDiagnosticsState(), QqListenerPluginInfo.Designer, false)
    {
        _diagnostics.RecordRuntimeInfo("设计器", "未通过插件宿主加载", "", GetType().Assembly.Location, null);
    }

    public QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo)
        : this(settings, diagnostics, pluginInfo, true)
    {
    }

    private QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo,
        bool autoSaveEnabled)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _pluginInfo = pluginInfo;
        _autoSaveEnabled = autoSaveEnabled;
        DataContext = this;
        InitializeComponent();

        if (_autoSaveEnabled)
        {
            _persistenceCoordinator = new QqListenerSettingsPersistenceCoordinator(
                _settings,
                _diagnostics,
                OnSettingsPropertyChanged);
        }
    }

    private void OnSettingsPropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QqListenerSettings.IsEnabled))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPoll)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanReplay)));
        }

        if (_persistenceCoordinator?.IsDirty == true)
        {
            DebounceSave();
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _persistenceCoordinator?.Attach();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _persistenceCoordinator?.Detach();
        if (_saveTimer != null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer = null;
        }

        base.OnDetachedFromVisualTree(e);
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
        _persistenceCoordinator?.SavePendingSettings();
    }

    // ===== 快捷操作 按钮 handlers =====

    private async void OnPollNowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;
            await provider.PollOnceManuallyAsync();
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "手动扫一次失败");
        }
    }

    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;
            provider.ResetDetectionState();
            _diagnostics.RecordIgnored("已手动重置去重与冷却状态");
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "重置去重失败");
        }
    }

    private void OnReplayLatestClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;

            var latest = IAppHost.GetService<LatestMessageState>();
            if (latest is not { HasMessage: true }) return; // 静默：无消息不弹

            var message = new MessageEvent(
                "qq.toast",
                string.IsNullOrWhiteSpace(latest.Source) ? "QQ" : latest.Source,
                latest.Sender,
                latest.Conversation,
                latest.Message,
                latest.ReceivedAt,
                latest.Importance,
                [latest.Sender, latest.Message],
                $"qq.toast:replay:{latest.Sender}");
            provider.ShowQqNotification(message);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "重示最后一条失败");
        }
    }

    private static QqNotificationProvider? TryGetProvider()
    {
        var services = IAppHost.Host?.Services;
        if (services == null) return null;
        return services.GetService<QqNotificationProvider>()
               ?? services.GetServices<IHostedService>().OfType<QqNotificationProvider>().FirstOrDefault();
    }
}
