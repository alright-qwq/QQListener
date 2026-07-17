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
    private readonly QqListenerWorker _worker;
    private readonly IQqNotificationPoller _poller;
    private readonly ClassIslandMessageNotifier _notifier;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly ILogger<QqNotificationProvider> _logger;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public QqNotificationProvider(
        QqListenerSettings settings,
        QqListenerWorker worker,
        IQqNotificationPoller poller,
        ClassIslandMessageNotifier notifier,
        QqListenerDiagnosticsState diagnostics,
        ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _worker = worker;
        _poller = poller;
        _notifier = notifier;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public void ResetDetectionState()
    {
        _poller.ResetDetectionState();
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_listenerTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _listenerCts?.Dispose();
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listenerTask = _worker.RunAsync(ShowQqNotificationAsync, _listenerCts.Token);
        }

        _logger.LogInformation(
            "QQListener 监听已启动（扫描间隔 {Interval}s，QqOnly={QqOnly}）",
            _settings.ScanIntervalSeconds,
            _settings.QqOnly);
        _diagnostics.MarkStarted(_settings.IsEnabled);
        return Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        Task? listenerTask;
        CancellationTokenSource? listenerCts;
        lock (_lifecycleLock)
        {
            listenerTask = _listenerTask;
            listenerCts = _listenerCts;
        }

        if (listenerTask is null || listenerCts is null) return;

        _logger.LogInformation("QQListener 监听正在停止...");
        await listenerCts.CancelAsync();
        await listenerTask.WaitAsync(cancellationToken);

        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_listenerTask, listenerTask))
            {
                _listenerTask = null;
                _listenerCts = null;
                listenerCts.Dispose();
            }
        }

        _logger.LogInformation("QQListener 监听已停止。");
        _diagnostics.MarkStopped();
    }

    public Task PollOnceManuallyAsync()
    {
        return _worker.PollOnceAndDispatchAsync(
            retryPermission: true,
            ShowQqNotificationAsync,
            CancellationToken.None);
    }

    public Task ReplayLatestAsync(CancellationToken cancellationToken = default)
    {
        return _worker.ReplayLatestAsync(
            (message, showIfCurrent, token) => _notifier.ShowAsync(
                message,
                request => showIfCurrent(() => ShowNotification(request)),
                token),
            cancellationToken);
    }

    private Task ShowQqNotificationAsync(MessageEvent message, CancellationToken cancellationToken)
    {
        return _notifier.ShowAsync(message, ShowNotification, cancellationToken);
    }
}
