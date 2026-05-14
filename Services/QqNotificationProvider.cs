using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

[NotificationProviderInfo("a6f94b7a-d398-41d4-b3c7-208fb9d9ad7b", "QQListener", "\uE715", "监听 QQ 通知并转发为 ClassIsland 提醒。")]
public class QqNotificationProvider : NotificationProviderBase, IHostedService
{
    private readonly QqListenerSettings _settings;
    private readonly WindowsNotificationReader _reader;
    private readonly QqMessageProcessor _processor;
    private readonly ILogger<QqNotificationProvider> _logger;
    private readonly HashSet<uint> _knownNotificationIds = [];
    private bool _hasInitializedNotifications;
    private CancellationTokenSource? _listenerCts;

    public QqNotificationProvider(
        QqListenerSettings settings,
        WindowsNotificationReader reader,
        QqMessageProcessor processor,
        ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _reader = reader;
        _processor = processor;
        _logger = logger;
    }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QQListener 监听已启动（扫描间隔 {Interval}s，QQOnly={QqOnly}）",
            _settings.ScanIntervalSeconds, _settings.QqOnly);

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ListenAsync(_listenerCts.Token), CancellationToken.None);
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
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QQ 通知监听轮询失败。");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.ScanIntervalSeconds), cancellationToken);
        }
    }

    private async Task PollOnceAsync()
    {
        if (!_settings.IsEnabled)
        {
            return;
        }

        var snapshots = await _reader.ReadToastNotificationsAsync();

        if (!_hasInitializedNotifications)
        {
            foreach (var snapshot in snapshots)
                _knownNotificationIds.Add(snapshot.Id);

            _processor.UpdateActiveToasts(snapshots.Select(x => x.Texts));
            _hasInitializedNotifications = true;

            _logger.LogInformation("QQListener 初始化完成，已忽略 {Count} 条现有通知。", snapshots.Count);
            return;
        }

        foreach (var snapshot in snapshots)
        {
            if (!_knownNotificationIds.Add(snapshot.Id)) continue;

            _logger.LogDebug("新通知 [{AppName}] Id={Id} Texts={Count}条",
                snapshot.AppName, snapshot.Id, snapshot.Texts.Count);

            if (_settings.QqOnly && !IsQqNotification(snapshot))
            {
                _logger.LogDebug("跳过非 QQ 通知 [{AppName}]", snapshot.AppName);
                continue;
            }

            var message = _processor.Process(snapshot.Texts);
            if (message != null)
            {
                _logger.LogInformation("收到 {Type}消息: {Sender} {MessagePreview}",
                    message.Calling ? "呼叫" : message.Important ? "重要" : "普通",
                    message.Sender,
                    Truncate(message.Message, 30));

                ShowQqNotification(message);
            }
        }

        _knownNotificationIds.IntersectWith(snapshots.Select(x => x.Id));
        _processor.UpdateActiveToasts(snapshots.Select(x => x.Texts));
    }

    private void ShowQqNotification(QqNotificationMessage message)
    {
        var title = message.Calling ? "QQ 呼叫" : message.Important ? "重要 QQ 消息" : "QQ 消息";
        var body = string.IsNullOrWhiteSpace(message.Message)
            ? message.Sender
            : $"{message.Sender}{Environment.NewLine}{message.Message}";

        void DoShow()
        {
            NotificationContent overlay;

            if (_settings.RollingSpeed <= 0)
            {
                overlay = NotificationContent.CreateSimpleTextContent(body, content =>
                {
                    content.Duration = message.Duration;
                    content.SpeechContent = body;
                });
            }
            else
            {
                var rollingDuration = TimeSpan.FromMilliseconds(message.Duration.TotalMilliseconds / Math.Max(0.01, _settings.RollingSpeed));
                overlay = NotificationContent.CreateRollingTextContent(body, rollingDuration, message.Calling ? 10 : 2);
            }

            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(title, rightIcon: "\uE715", factory: content =>
                {
                    content.Duration = TimeSpan.FromSeconds(2);
                    content.SpeechContent = title;
                }),
                OverlayContent = overlay,
                RequestNotificationSettings =
                {
                    IsSettingsEnabled = true,
                    IsSpeechEnabled = true,
                    IsNotificationEffectEnabled = true,
                    IsNotificationSoundEnabled = true,
                    IsNotificationTopmostEnabled = message.Calling
                }
            });
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            DoShow();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(DoShow);
    }

    private static bool IsQqNotification(WindowsToastSnapshot snapshot)
    {
        return snapshot.AppName.Equals("QQ", StringComparison.OrdinalIgnoreCase)
               || snapshot.AppName.Contains("QQ", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? "";
        return value[..maxLength] + "…";
    }
}
