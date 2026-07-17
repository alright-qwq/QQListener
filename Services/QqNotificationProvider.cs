using System.ComponentModel;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

[NotificationProviderInfo("a6f94b7a-d398-41d4-b3c7-208fb9d9ad7b", "QQListener", "\uE02B", "监听 QQ 通知并转发为 ClassIsland 提醒。")]
public class QqNotificationProvider : NotificationProviderBase, IHostedService
{
    private readonly QqListenerSettings _settings;
    private readonly IQqNotificationPoller _poller;
    private readonly ClassIslandMessageNotifier _notifier;
    private readonly LatestMessageState _latestMessage;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly ILogger<QqNotificationProvider> _logger;
    private CancellationTokenSource? _listenerCts;

    public QqNotificationProvider(
        QqListenerSettings settings,
        IQqNotificationPoller poller,
        ClassIslandMessageNotifier notifier,
        LatestMessageState latestMessage,
        QqListenerDiagnosticsState diagnostics,
        ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _poller = poller;
        _notifier = notifier;
        _latestMessage = latestMessage;
        _diagnostics = diagnostics;
        _logger = logger;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QqListenerSettings.IsEnabled) && _settings.IsEnabled)
        {
            ResetDetectionState();
        }

        if (e.PropertyName == nameof(QqListenerSettings.IsEnabled))
        {
            _diagnostics.MarkEnabledChanged(_settings.IsEnabled);
        }
    }

    public void ResetDetectionState()
    {
        _poller.ResetDetectionState();
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QQListener 监听已启动（扫描间隔 {Interval}s，QqOnly={QqOnly}）",
            _settings.ScanIntervalSeconds, _settings.QqOnly);
        _diagnostics.MarkStarted(_settings.IsEnabled);

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ListenAsync(_listenerCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        if (_listenerCts == null) return;

        try
        {
            _logger.LogInformation("QQListener 监听正在停止...");
            await _listenerCts.CancelAsync();
        }
        finally
        {
            _listenerCts.Dispose();
            _listenerCts = null;
        }

        _logger.LogInformation("QQListener 监听已停止。");
        _diagnostics.MarkStopped();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(retryPermission: false, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QQ 通知监听轮询失败。");
                _diagnostics.RecordError(ex, "监听轮询失败");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.ScanIntervalSeconds), cancellationToken);
        }
    }

    private async Task PollOnceAsync(bool retryPermission, CancellationToken cancellationToken)
    {
        if (!_settings.IsEnabled)
        {
            _diagnostics.MarkEnabledChanged(false);
            return;
        }

        var pollResult = await _poller.PollAsync(retryPermission, cancellationToken);
        if (!pollResult.IsSuccess)
        {
            _logger.LogDebug(
                "跳过本次通知状态更新：{Status} {Reason}",
                pollResult.ReadStatus,
                pollResult.Reason);
            return;
        }

        foreach (var message in pollResult.AcceptedEvents)
        {
            _latestMessage.Update(message);
            ShowQqNotification(message);
        }
    }

    public Task PollOnceManuallyAsync()
    {
        return PollOnceAsync(retryPermission: true, CancellationToken.None);
    }

    public void ShowQqNotification(MessageEvent message)
    {
        _notifier.Show(message, ShowNotification);
    }
}
