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
    private readonly WindowsNotificationReader _reader;
    private readonly QqToastMessageParser _parser;
    private readonly MessageEventPipeline _pipeline;
    private readonly ClassIslandMessageNotifier _notifier;
    private readonly LatestMessageState _latestMessage;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly ILogger<QqNotificationProvider> _logger;
    private readonly Dictionary<uint, int> _knownNotificationKeys = new();
    private volatile bool _hasInitializedNotifications;
    private CancellationTokenSource? _listenerCts;
    private readonly object _pollLock = new();

    public QqNotificationProvider(
        QqListenerSettings settings,
        WindowsNotificationReader reader,
        QqToastMessageParser parser,
        MessageEventPipeline pipeline,
        ClassIslandMessageNotifier notifier,
        LatestMessageState latestMessage,
        QqListenerDiagnosticsState diagnostics,
        ILogger<QqNotificationProvider> logger)
    {
        _settings = settings;
        _reader = reader;
        _parser = parser;
        _pipeline = pipeline;
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

    private void ResetDetectionState()
    {
        lock (_pollLock)
        {
            _knownNotificationKeys.Clear();
            _pipeline.ClearState();
            _hasInitializedNotifications = false;
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QQListener 监听已启动（扫描间隔 {Interval}s，QQOnly={QqOnly}）",
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
                await PollOnceAsync();
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

    private async Task PollOnceAsync()
    {
        if (!_settings.IsEnabled)
        {
            _diagnostics.MarkEnabledChanged(false);
            return;
        }

        var allSnapshots = await _reader.ReadToastNotificationsAsync();
        var snapshots = (_settings.QqOnly
                ? allSnapshots.Where(IsQqCandidateNotification)
                : allSnapshots)
            .ToArray();

        List<MessageEvent>? pendingMessages = null;

        lock (_pollLock)
        {
            if (!_hasInitializedNotifications)
            {
                foreach (var snapshot in snapshots)
                {
                    _knownNotificationKeys[snapshot.Id] = GetSnapshotKey(snapshot);
                }
                _pipeline.UpdateActiveEvents(ParseVisibleEvents(snapshots));
                _hasInitializedNotifications = true;
                _diagnostics.RecordIgnored($"初始化完成，已忽略 {snapshots.Length} 条现有通知");
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

                if (!_parser.TryParse(snapshot.Texts, DateTimeOffset.Now, out var messageEvent, out var parseReason)
                    || messageEvent == null)
                {
                    _diagnostics.RecordIgnored(parseReason);
                    _logger.LogDebug("跳过无法解析的通知: {Reason}", parseReason);
                    continue;
                }

                var result = _pipeline.Process(messageEvent);
                if (result.Accepted && result.Event != null)
                {
                    _logger.LogInformation("收到 {Type}消息: {Sender} {MessagePreview} Reason={Reason}",
                        FormatImportance(result.Event.Importance),
                        result.Event.Sender,
                        Truncate(result.Event.Body, 30),
                        result.Reason);

                    pendingMessages ??= new List<MessageEvent>();
                    pendingMessages.Add(result.Event);
                }
            }

            var visibleIds = snapshots.Select(x => x.Id).ToHashSet();
            foreach (var id in _knownNotificationKeys.Keys.Where(x => !visibleIds.Contains(x)).ToArray())
            {
                _knownNotificationKeys.Remove(id);
            }
            _pipeline.UpdateActiveEvents(ParseVisibleEvents(snapshots));
        }

        // Dispatch UI updates outside the lock to avoid potential deadlocks
        // when subscribers try to acquire _pollLock from the UI thread.
        if (pendingMessages != null)
        {
            foreach (var message in pendingMessages)
            {
                _latestMessage.Update(message);
                ShowQqNotification(message);
            }
        }
    }

    private IEnumerable<MessageEvent> ParseVisibleEvents(IEnumerable<WindowsToastSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (_parser.TryParse(snapshot.Texts, DateTimeOffset.Now, out var messageEvent, out _)
                && messageEvent != null)
            {
                yield return messageEvent;
            }
        }
    }

    public void ShowQqNotification(MessageEvent message)
    {
        _notifier.Show(message, ShowNotification);
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
        return value[..maxLength] + "...";
    }

    private static string FormatImportance(MessageImportance importance)
    {
        return importance switch
        {
            MessageImportance.Calling => "呼叫",
            MessageImportance.Important => "重要",
            MessageImportance.Normal => "普通",
            MessageImportance.Ignored => "忽略",
            _ => importance.ToString()
        };
    }
}
