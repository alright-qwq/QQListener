using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public sealed class QqNotificationPoller(
    QqListenerSettings settings,
    IWindowsNotificationReader reader,
    QqToastMessageParser parser,
    MessageEventPipeline pipeline,
    QqListenerDiagnosticsState diagnostics,
    ILogger<QqNotificationPoller> logger) : IQqNotificationPoller
{
    private readonly Dictionary<uint, string> _knownNotificationKeys = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly object _stateLock = new();
    private bool _hasInitializedNotifications;
    private long _resetGeneration;

    public async Task<QqNotificationPollResult> PollAsync(
        bool retryPermission,
        CancellationToken cancellationToken)
    {
        await _pollGate.WaitAsync(cancellationToken);
        try
        {
            long generation;
            lock (_stateLock)
            {
                generation = _resetGeneration;
            }

            var readResult = await reader.ReadToastNotificationsAsync(
                retryPermission,
                cancellationToken);
            if (!readResult.IsSuccess)
            {
                return new QqNotificationPollResult(
                    readResult.Status,
                    [],
                    readResult.Reason);
            }

            var allSnapshots = readResult.Notifications;
            var snapshots = allSnapshots
                .Where(snapshot => QqNotificationCandidateFilter.IsCandidate(snapshot, settings.QqOnly))
                .ToArray();

            lock (_stateLock)
            {
                if (generation != _resetGeneration)
                {
                    return new QqNotificationPollResult(
                        WindowsNotificationReadStatus.Success,
                        [],
                        "检测状态已重置，已丢弃在途读取结果");
                }

                diagnostics.RecordCandidateFilter(allSnapshots.Count, snapshots.Length, settings.QqOnly);

                if (!_hasInitializedNotifications)
                {
                    foreach (var snapshot in snapshots)
                    {
                        _knownNotificationKeys[snapshot.Id] = GetSnapshotKey(snapshot);
                    }

                    pipeline.UpdateActiveEvents(ParseVisibleEvents(snapshots));
                    _hasInitializedNotifications = true;
                    var reason = $"初始化完成，已忽略 {snapshots.Length} 条现有通知";
                    diagnostics.RecordIgnored(reason);
                    logger.LogInformation(
                        "QQListener 初始化完成，已忽略 {Count} 条现有 QQ 通知。",
                        snapshots.Length);
                    return new QqNotificationPollResult(
                        WindowsNotificationReadStatus.Success,
                        [],
                        reason);
                }

                var acceptedEvents = new List<MessageEvent>();
                foreach (var snapshot in snapshots)
                {
                    var snapshotKey = GetSnapshotKey(snapshot);
                    if (_knownNotificationKeys.TryGetValue(snapshot.Id, out var knownKey)
                        && knownKey == snapshotKey)
                    {
                        continue;
                    }

                    _knownNotificationKeys[snapshot.Id] = snapshotKey;
                    logger.LogDebug(
                        "新通知或通知内容变化 [{AppName}] AppId={AppId} Id={Id} Texts={Count}条",
                        snapshot.AppName,
                        snapshot.AppId,
                        snapshot.Id,
                        snapshot.Texts.Count);

                    if (!parser.TryParse(
                            snapshot.Texts,
                            DateTimeOffset.Now,
                            out var messageEvent,
                            out var parseReason)
                        || messageEvent is null)
                    {
                        diagnostics.RecordIgnored(parseReason);
                        logger.LogDebug("跳过无法解析的通知: {Reason}", parseReason);
                        continue;
                    }

                    var processResult = pipeline.Process(messageEvent);
                    if (!processResult.Accepted || processResult.Event is null)
                    {
                        continue;
                    }

                    logger.LogInformation(
                        "收到消息: Importance={Importance} TextLength={TextLength} Source={Source} Reason={Reason}",
                        FormatImportance(processResult.Event.Importance),
                        processResult.Event.Body.Length,
                        processResult.Event.SourceDisplayName,
                        processResult.Reason);
                    acceptedEvents.Add(processResult.Event);
                }

                var visibleIds = allSnapshots.Select(snapshot => snapshot.Id).ToHashSet();
                foreach (var id in _knownNotificationKeys.Keys
                             .Where(id => !visibleIds.Contains(id))
                             .ToArray())
                {
                    _knownNotificationKeys.Remove(id);
                }

                pipeline.UpdateActiveEvents(ParseVisibleEvents(snapshots));
                return new QqNotificationPollResult(
                    WindowsNotificationReadStatus.Success,
                    acceptedEvents,
                    $"已接收 {acceptedEvents.Count} 条新消息");
            }
        }
        finally
        {
            _pollGate.Release();
        }
    }

    public void ResetDetectionState()
    {
        lock (_stateLock)
        {
            unchecked
            {
                _resetGeneration++;
            }

            _knownNotificationKeys.Clear();
            pipeline.ClearState();
            _hasInitializedNotifications = false;
        }
    }

    private IEnumerable<MessageEvent> ParseVisibleEvents(
        IEnumerable<WindowsToastSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (parser.TryParse(snapshot.Texts, DateTimeOffset.Now, out var messageEvent, out _)
                && messageEvent is not null)
            {
                yield return messageEvent;
            }
        }
    }

    private static string GetSnapshotKey(WindowsToastSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.AppName?.ToLowerInvariant() ?? "");
        builder.Append('\u001f');
        builder.Append(snapshot.AppId?.ToLowerInvariant() ?? "");
        foreach (var text in snapshot.Texts)
        {
            builder.Append('\u001f');
            builder.Append(text);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string FormatImportance(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => "呼叫",
        MessageImportance.Important => "重要",
        MessageImportance.Normal => "普通",
        MessageImportance.Ignored => "忽略",
        _ => importance.ToString()
    };
}
