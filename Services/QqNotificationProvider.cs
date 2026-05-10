using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QQListener.Models;
using System;

namespace QQListener.Services;

[NotificationProviderInfo("a6f94b7a-d398-41d4-b3c7-208fb9d9ad7b", "QQListener", "\uE715", "监听 QQ 通知并转发为 ClassIsland 提醒。")]
public class QqNotificationProvider : NotificationProviderBase, IHostedService
{
    private readonly QqListenerSettings _settings;
    private readonly WindowsNotificationReader _reader = new();
    private readonly QqMessageProcessor _processor;
    private readonly ILogger<QqNotificationProvider> _logger;
    private readonly HashSet<uint> _knownNotificationIds = [];
    private bool _hasInitializedNotifications;
    private CancellationTokenSource? _listenerCts;

    public static QqNotificationProvider? Instance { get; private set; }

    public QqNotificationProvider(QqListenerSettings settings, ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _settings.Normalize();
        _settings.Save();
        _logger = logger;
        _processor = new QqMessageProcessor(_settings);
        Instance = this;
    }

    public new Task StartAsync(CancellationToken cancellationToken)
    {
        return StartListenerAsync(cancellationToken);
    }

    public new async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopListenerAsync();
    }

    public Task StartListenerAsync(CancellationToken cancellationToken = default)
    {
        if (_listenerCts != null)
        {
            return Task.CompletedTask;
        }

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ListenAsync(_listenerCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopListenerAsync()
    {
        if (_listenerCts == null)
        {
            return;
        }

        try
        {
            await _listenerCts.CancelAsync();
        }
        finally
        {
            _listenerCts.Dispose();
            _listenerCts = null;
        }
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

        _settings.Normalize();
        var snapshots = await _reader.ReadToastNotificationsAsync();
        if (!_hasInitializedNotifications)
        {
            foreach (var snapshot in snapshots)
            {
                _knownNotificationIds.Add(snapshot.Id);
            }

            _processor.UpdateActiveToasts(snapshots.Select(x => x.Texts));
            _hasInitializedNotifications = true;
            return;
        }

        foreach (var snapshot in snapshots)
        {
            if (!_knownNotificationIds.Add(snapshot.Id))
            {
                continue;
            }

            if (_settings.QqOnly && !IsQqNotification(snapshot))
            {
                continue;
            }

            var message = _processor.Process(snapshot.Texts);
            if (message != null)
            {
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

        void doShow()
        {
            NotificationContent overlay;

            if (_settings.RollingSpeed <= 0)
            {
                // No scrolling: show static simple text for the same duration computed by the processor            
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
        {
            doShow();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(doShow);
        }
    }

    private static bool IsQqNotification(WindowsToastSnapshot snapshot)
    {
        return snapshot.AppName.Equals("QQ", StringComparison.OrdinalIgnoreCase)
               || snapshot.AppName.Contains("QQ", StringComparison.OrdinalIgnoreCase);
    }
}
