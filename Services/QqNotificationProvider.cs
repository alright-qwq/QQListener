using System.ComponentModel;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

[NotificationProviderInfo("a6f94b7a-d398-41d4-b3c7-208fb9d9ad7b", "QQListener", "\uE02B", "监听 QQ 通知并转发为 ClassIsland 提醒。")]
public class QqNotificationProvider : NotificationProviderBase, IHostedService
{
    private readonly QqListenerSettings _settings;
    private readonly WindowsNotificationReader _reader;
    private readonly QqMessageProcessor _processor;
    private readonly LatestMessageState _latestMessage;
    private readonly ILogger<QqNotificationProvider> _logger;
    private readonly Dictionary<uint, int> _knownNotificationKeys = new();
    private volatile bool _hasInitializedNotifications;
    private CancellationTokenSource? _listenerCts;
    private readonly object _pollLock = new();

    public QqNotificationProvider(
        QqListenerSettings settings,
        WindowsNotificationReader reader,
        QqMessageProcessor processor,
        LatestMessageState latestMessage,
        ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _reader = reader;
        _processor = processor;
        _latestMessage = latestMessage;
        _logger = logger;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QqListenerSettings.IsEnabled) && _settings.IsEnabled)
        {
            ResetDetectionState();
        }
    }

    private void ResetDetectionState()
    {
        lock (_pollLock)
        {
            _knownNotificationKeys.Clear();
            _processor.ClearState();
            _hasInitializedNotifications = false;
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QQListener 监听已启动（扫描间隔 {Interval}s，QQOnly={QqOnly}）",
            _settings.ScanIntervalSeconds, _settings.QqOnly);

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

        var allSnapshots = await _reader.ReadToastNotificationsAsync();
        if (allSnapshots == null) return;
        var snapshots = (_settings.QqOnly
                ? allSnapshots.Where(IsQqCandidateNotification)
                : allSnapshots)
            .ToArray();

        List<QqNotificationMessage>? pendingMessages = null;

        lock (_pollLock)
        {
            if (!_hasInitializedNotifications)
            {
                foreach (var snapshot in snapshots)
                {
                    _knownNotificationKeys[snapshot.Id] = GetSnapshotKey(snapshot);
                }
                _processor.UpdateActiveToasts(snapshots.Select(x => x.Texts));
                _hasInitializedNotifications = true;
                _logger.LogInformation("QQListener 初始化完成，已忽略 {Count} 条现有 QQ 通知。", snapshots.Length);
                return;
            }

            foreach (var snapshot in snapshots)
            {
                var snapshotKey = GetSnapshotKey(snapshot);
                if (_knownNotificationKeys.TryGetValue(snapshot.Id, out var knownKey) && knownKey == snapshotKey) continue;
                _knownNotificationKeys[snapshot.Id] = snapshotKey;

                _logger.LogDebug("新通知或通知内容变化 [{AppName}] AppId={AppId} Id={Id} Texts={Count}条",
                    snapshot.AppName, snapshot.AppId, snapshot.Id, snapshot.Texts.Count);

                if (_settings.QqOnly && !IsQqCandidateNotification(snapshot))
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

                    pendingMessages ??= new List<QqNotificationMessage>();
                    pendingMessages.Add(message);
                }
            }

            var visibleIds = snapshots.Select(x => x.Id).ToHashSet();
            foreach (var id in _knownNotificationKeys.Keys.Where(x => !visibleIds.Contains(x)).ToArray())
            {
                _knownNotificationKeys.Remove(id);
            }
            _processor.UpdateActiveToasts(snapshots.Select(x => x.Texts));
        }

        // Dispatch UI updates outside the lock to avoid potential deadlocks
        // when subscribers try to acquire _pollLock from the UI thread.
        if (pendingMessages != null)
        {
            foreach (var message in pendingMessages)
            {
                _latestMessage.Update(message.Sender, message.Message, message.Important, message.Calling);
                ShowQqNotification(message);
            }
        }
    }

    public void ShowQqNotification(QqNotificationMessage message)
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
                MaskContent = NotificationContent.CreateTwoIconsMask(title, rightIcon: "\uE02B", factory: content =>
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

    private static int GetSnapshotKey(WindowsToastSnapshot snapshot)
    {
        var hash = new HashCode();
        hash.Add(snapshot.AppName, StringComparer.OrdinalIgnoreCase);
        hash.Add(snapshot.AppId, StringComparer.OrdinalIgnoreCase);
        foreach (var text in snapshot.Texts)
        {
            hash.Add(text);
        }
        return hash.ToHashCode();
    }

    private static bool IsQqCandidateNotification(WindowsToastSnapshot snapshot)
    {
        if (IsQqNotification(snapshot)) return true;
        if (LooksLikeQqMessage(snapshot.Texts)) return true;
        if (string.IsNullOrWhiteSpace(snapshot.AppName) && string.IsNullOrWhiteSpace(snapshot.AppId))
            return LooksLikeQqMessageContent(snapshot.Texts);
        return false;
    }

    private static bool IsQqNotification(WindowsToastSnapshot snapshot)
    {
        return ContainsQqIdentity(snapshot.AppName) || ContainsQqIdentity(snapshot.AppId);
    }

    private static bool ContainsQqIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("QQ", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Tim", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Tencent", StringComparison.OrdinalIgnoreCase)
               || value.Contains("com.tencent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeQqMessage(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return false;
        var fullText = string.Join('\n', texts);
        return fullText.Contains("QQ", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("TIM", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("有人@我", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("群聊", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeQqMessageContent(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return false;
        var fullText = string.Join('\n', texts);
        return fullText.Contains("有人@我", StringComparison.OrdinalIgnoreCase)
               || fullText.Contains("群聊", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? "";
        return value[..maxLength] + "…";
    }
}
