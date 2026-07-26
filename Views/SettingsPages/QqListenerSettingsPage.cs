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
    private readonly MessageRulePreviewService _rulePreviewService;
    private readonly bool _autoSaveEnabled;
    private readonly QqListenerSettingsPersistenceCoordinator? _persistenceCoordinator;
    private DispatcherTimer? _saveTimer;
    private bool _providerAvailableCache;
    private bool _providerProbed;
    private string _previewToastText = "";
    private string _previewSourceText = "qq.toast";
    private string _previewTimeText = "";
    private string _previewResultText = "尚未测试";

    public QqListenerSettings Settings => _settings;
    public QqListenerDiagnosticsState Diagnostics => _diagnostics;
    public QqListenerPluginInfo PluginInfo => _pluginInfo;
    public string PreviewToastText
    {
        get => _previewToastText;
        set => SetPreviewProperty(ref _previewToastText, value, nameof(PreviewToastText));
    }

    public string PreviewSourceText
    {
        get => _previewSourceText;
        set => SetPreviewProperty(ref _previewSourceText, value, nameof(PreviewSourceText));
    }

    public string PreviewTimeText
    {
        get => _previewTimeText;
        set => SetPreviewProperty(ref _previewTimeText, value, nameof(PreviewTimeText));
    }

    public string PreviewResultText
    {
        get => _previewResultText;
        private set => SetPreviewProperty(ref _previewResultText, value, nameof(PreviewResultText));
    }

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

    public QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo,
        MessageRulePreviewService rulePreviewService)
        : this(settings, diagnostics, pluginInfo, true, rulePreviewService)
    {
    }

    private QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo,
        bool autoSaveEnabled)
        : this(
            settings,
            diagnostics,
            pluginInfo,
            autoSaveEnabled,
            new MessageRulePreviewService(
                new QqToastMessageParser(),
                new MessageRuleEvaluator(settings),
                TimeProvider.System))
    {
    }

    private QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo,
        bool autoSaveEnabled,
        MessageRulePreviewService rulePreviewService)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _pluginInfo = pluginInfo;
        _rulePreviewService = rulePreviewService;
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

    private async void OnReplayLatestClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (!_settings.IsEnabled) return;

            var provider = TryGetProvider();
            if (provider == null || !_settings.IsEnabled) return;

            await provider.ReplayLatestAsync();
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "重示最后一条失败");
        }
    }

    private void OnPreviewRulesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = _rulePreviewService.Preview(PreviewToastText, PreviewSourceText, PreviewTimeText);
        PreviewResultText = result.Success
            ? $"{GetImportanceDisplay(result.Importance)} / {result.Reason}"
            : result.Reason;
    }

    private void OnApplyHomeworkPresetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ApplyPreset(QqListenerRulePreset.HomeworkReminder);

    private void OnApplyTeacherCallingPresetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ApplyPreset(QqListenerRulePreset.TeacherCalling);

    private void OnApplyExamNoticePresetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ApplyPreset(QqListenerRulePreset.ExamNotice);

    private void ApplyPreset(QqListenerRulePreset preset) =>
        _settings.ModifyAndSave(settings => QqListenerRulePresets.Apply(settings, preset));

    private void SetPreviewProperty(ref string field, string value, string propertyName)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetImportanceDisplay(MessageImportance importance) => importance switch
    {
        MessageImportance.Normal => "普通",
        MessageImportance.Important => "重要",
        MessageImportance.Calling => "呼叫",
        MessageImportance.Ignored => "已忽略",
        _ => "未知"
    };

    private static QqNotificationProvider? TryGetProvider()
    {
        var services = IAppHost.Host?.Services;
        if (services == null) return null;
        return services.GetService<QqNotificationProvider>()
               ?? services.GetServices<IHostedService>().OfType<QqNotificationProvider>().FirstOrDefault();
    }
}
