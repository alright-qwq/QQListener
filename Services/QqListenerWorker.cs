using System.ComponentModel;
using QQListener.Models;

namespace QQListener.Services;

public sealed class QqListenerWorker
{
    private readonly QqListenerSettings _settings;
    private readonly IQqNotificationPoller _poller;
    private readonly LatestMessageState _latestMessage;
    private readonly IQqListenerDelay _delay;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly MessageAutomationEventHub _automationEventHub;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _pollDispatchGate = new(1, 1);
    private long _generation;
    private CancellationTokenSource _generationCancellation = new();
    private TaskCompletionSource<bool> _settingsChanged = CreateSignal();

    public QqListenerWorker(
        QqListenerSettings settings,
        IQqNotificationPoller poller,
        LatestMessageState latestMessage,
        IQqListenerDelay delay,
        QqListenerDiagnosticsState diagnostics,
        MessageAutomationEventHub automationEventHub)
    {
        _settings = settings;
        _poller = poller;
        _latestMessage = latestMessage;
        _delay = delay;
        _diagnostics = diagnostics;
        _automationEventHub = automationEventHub;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public async Task RunAsync(
        Func<MessageEvent, CancellationToken, Task> dispatchAsync,
        CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_settings.IsEnabled)
                {
                    consecutiveFailures = 0;
                    await WaitForDelayOrSettingsChangeAsync(
                        GetScanInterval(),
                        cancellationToken);
                    continue;
                }

                var result = await PollOnceAndDispatchAsync(
                    retryPermission: false,
                    dispatchAsync,
                    cancellationToken);

                if (!_settings.IsEnabled)
                {
                    consecutiveFailures = 0;
                    continue;
                }

                TimeSpan delay;
                if (result.IsSuccess)
                {
                    consecutiveFailures = 0;
                    delay = GetScanInterval();
                }
                else
                {
                    consecutiveFailures++;
                    delay = GetFailureDelay(consecutiveFailures);
                }

                await WaitForDelayOrSettingsChangeAsync(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task<QqNotificationPollResult> PollOnceAndDispatchAsync(
        bool retryPermission,
        Func<MessageEvent, CancellationToken, Task> dispatchAsync,
        CancellationToken cancellationToken)
    {
        await _pollDispatchGate.WaitAsync(cancellationToken);
        try
        {
            return await PollOnceAndDispatchCoreAsync(
                retryPermission,
                dispatchAsync,
                cancellationToken);
        }
        finally
        {
            _pollDispatchGate.Release();
        }
    }

    private async Task<QqNotificationPollResult> PollOnceAndDispatchCoreAsync(
        bool retryPermission,
        Func<MessageEvent, CancellationToken, Task> dispatchAsync,
        CancellationToken cancellationToken)
    {
        long generation;
        CancellationToken generationToken;
        lock (_stateLock)
        {
            if (!_settings.IsEnabled)
            {
                return DisabledResult();
            }

            generation = _generation;
            generationToken = _generationCancellation.Token;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            generationToken);
        QqNotificationPollResult result;
        try
        {
            result = await _poller.PollAsync(retryPermission, linkedCancellation.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && generationToken.IsCancellationRequested)
        {
            return ChangedGenerationResult();
        }

        if (!result.IsSuccess)
        {
            return result;
        }

        try
        {
            foreach (var message in result.AcceptedEvents)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                lock (_stateLock)
                {
                    if (generation != _generation
                        || !_settings.IsEnabled
                        || linkedCancellation.IsCancellationRequested)
                    {
                        return ChangedGenerationResult();
                    }

                    _latestMessage.Update(message);
                }

                if (!IsGenerationCurrent(generation, generationToken))
                {
                    return ChangedGenerationResult();
                }

                linkedCancellation.Token.ThrowIfCancellationRequested();
                _automationEventHub.Publish(message);

                if (!IsGenerationCurrent(generation, generationToken))
                {
                    return ChangedGenerationResult();
                }

                linkedCancellation.Token.ThrowIfCancellationRequested();
                await dispatchAsync(message, linkedCancellation.Token);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && generationToken.IsCancellationRequested)
        {
            return ChangedGenerationResult();
        }

        return result;
    }

    public async Task ReplayLatestAsync(
        Func<MessageEvent, Action<Action>, CancellationToken, Task> dispatchAsync,
        CancellationToken cancellationToken)
    {
        long generation;
        CancellationToken generationToken;
        lock (_stateLock)
        {
            if (!_settings.IsEnabled) return;

            generation = _generation;
            generationToken = _generationCancellation.Token;
        }

        if (!_latestMessage.TryCreateReplayEvent(out var message)) return;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            generationToken);

        bool CanShow()
        {
            if (linkedCancellation.IsCancellationRequested) return false;

            lock (_stateLock)
            {
                return generation == _generation
                       && _settings.IsEnabled
                       && !linkedCancellation.IsCancellationRequested;
            }
        }

        void ShowIfCurrent(Action show)
        {
            if (linkedCancellation.IsCancellationRequested) return;

            lock (_stateLock)
            {
                if (generation == _generation
                    && _settings.IsEnabled
                    && !linkedCancellation.IsCancellationRequested)
                {
                    show();
                }
            }
        }

        if (!CanShow()) return;

        try
        {
            await dispatchAsync(message, ShowIfCurrent, linkedCancellation.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && generationToken.IsCancellationRequested)
        {
        }
    }

    public void ResetDetectionState()
    {
        _poller.ResetDetectionState();
    }

    private bool IsGenerationCurrent(long generation, CancellationToken generationToken)
    {
        lock (_stateLock)
        {
            return generation == _generation
                   && _settings.IsEnabled
                   && !generationToken.IsCancellationRequested;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QqListenerSettings.IsEnabled)) return;

        CancellationTokenSource previousGenerationCancellation;
        TaskCompletionSource<bool> previousSignal;
        lock (_stateLock)
        {
            _generation++;
            previousGenerationCancellation = _generationCancellation;
            _generationCancellation = new CancellationTokenSource();
            previousSignal = Interlocked.Exchange(ref _settingsChanged, CreateSignal());

            if (_settings.IsEnabled)
            {
                _poller.ResetDetectionState();
            }
            else
            {
                _latestMessage.Clear();
            }

            _diagnostics.MarkEnabledChanged(_settings.IsEnabled);
        }

        previousGenerationCancellation.Cancel();
        previousGenerationCancellation.Dispose();
        previousSignal.TrySetResult(true);
    }

    private async Task WaitForDelayOrSettingsChangeAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var signal = Volatile.Read(ref _settingsChanged).Task;
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = _delay.DelayAsync(delay, delayCancellation.Token);
        var completed = await Task.WhenAny(delayTask, signal);
        if (completed == delayTask)
        {
            await delayTask;
            return;
        }

        await delayCancellation.CancelAsync();
        try
        {
            await delayTask;
        }
        catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
        {
        }
    }

    private TimeSpan GetScanInterval() =>
        TimeSpan.FromSeconds(Math.Clamp(_settings.ScanIntervalSeconds, 0.1, 5));

    private static TimeSpan GetFailureDelay(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 5);
        return TimeSpan.FromSeconds(Math.Min(30, 1 << exponent));
    }

    private static QqNotificationPollResult DisabledResult() => new(
        WindowsNotificationReadStatus.Success,
        [],
        "监听已关闭");

    private static QqNotificationPollResult ChangedGenerationResult() => new(
        WindowsNotificationReadStatus.Success,
        [],
        "监听状态已改变，已丢弃在途结果");

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
