using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public class QqListenerWorkerTests
{
    [Fact]
    public async Task DisableDuringPollDropsResultAndClearsLatestMessage()
    {
        var poller = new BlockingPoller(Event("private"));
        var settings = new QqListenerSettings { IsEnabled = true };
        var latest = new LatestMessageState();
        latest.Update(Event("old"));
        var dispatched = new List<MessageEvent>();
        var worker = CreateWorker(settings, poller, latest);

        var run = worker.PollOnceAndDispatchAsync(false, (message, _) =>
        {
            dispatched.Add(message);
            return Task.CompletedTask;
        }, default);
        await poller.Started.WaitAsync(TimeSpan.FromSeconds(2));
        settings.IsEnabled = false;
        poller.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(dispatched);
        Assert.False(latest.HasMessage);
    }

    [Fact]
    public async Task DisableThenEnableDuringPollDropsOldGeneration()
    {
        var poller = new BlockingPoller(Event("stale"));
        var settings = new QqListenerSettings { IsEnabled = true };
        var latest = new LatestMessageState();
        var dispatched = new List<MessageEvent>();
        var worker = CreateWorker(settings, poller, latest);

        var run = worker.PollOnceAndDispatchAsync(false, (message, _) =>
        {
            dispatched.Add(message);
            return Task.CompletedTask;
        }, default);
        await poller.Started.WaitAsync(TimeSpan.FromSeconds(2));
        settings.IsEnabled = false;
        settings.IsEnabled = true;
        poller.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(dispatched);
        Assert.False(latest.HasMessage);
        Assert.Equal(1, poller.ResetCount);
    }

    [Fact]
    public async Task DisableDuringBatchPreventsRemainingDispatches()
    {
        var settings = new QqListenerSettings { IsEnabled = true };
        var latest = new LatestMessageState();
        var poller = new SequencePoller(Accepted(Event("first"), Event("second")));
        var dispatched = new List<MessageEvent>();
        var worker = CreateWorker(settings, poller, latest);

        await worker.PollOnceAndDispatchAsync(false, (message, _) =>
        {
            dispatched.Add(message);
            settings.IsEnabled = false;
            return Task.CompletedTask;
        }, default);

        Assert.Collection(dispatched, message => Assert.Equal("first", message.Body));
        Assert.False(latest.HasMessage);
    }

    [Fact]
    public async Task RunAsyncCompletesWhenCancelledDuringARead()
    {
        using var cts = new CancellationTokenSource();
        var poller = new CancellablePoller();
        var worker = CreateWorker(new QqListenerSettings(), poller, new LatestMessageState());

        var run = worker.RunAsync((_, _) => Task.CompletedTask, cts.Token);
        await poller.Started.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(poller.CancellationObserved);
    }

    [Fact]
    public async Task FailureBackoffCapsAtThirtySecondsAndSuccessRestoresScanInterval()
    {
        using var cts = new CancellationTokenSource();
        var poller = new SequencePoller(
            Failed(), Failed(), Failed(), Failed(), Failed(), Failed(), Accepted());
        var delay = new RecordingDelay(cts, cancelAfter: 7);
        var settings = new QqListenerSettings { ScanIntervalSeconds = 0.25 };
        var worker = CreateWorker(settings, poller, new LatestMessageState(), delay);

        await worker.RunAsync((_, _) => Task.CompletedTask, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [1d, 2d, 4d, 8d, 16d, 30d, 0.25d],
            delay.Durations.Select(value => value.TotalSeconds).ToArray());
    }

    [Fact]
    public async Task EnableWakesFailureBackoffAndStartsNextPoll()
    {
        using var cts = new CancellationTokenSource();
        var settings = new QqListenerSettings { IsEnabled = true };
        var poller = new WakePoller();
        var delay = new BlockingDelay();
        var worker = CreateWorker(settings, poller, new LatestMessageState(), delay);

        var run = worker.RunAsync((_, _) => Task.CompletedTask, cts.Token);
        await delay.Started.WaitAsync(TimeSpan.FromSeconds(2));
        settings.IsEnabled = false;
        settings.IsEnabled = true;

        await poller.SecondPollStarted.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(delay.CancellationObserved);
        Assert.Equal(1, poller.ResetCount);
    }

    private static QqListenerWorker CreateWorker(
        QqListenerSettings settings,
        IQqNotificationPoller poller,
        LatestMessageState latest,
        IQqListenerDelay? delay = null)
    {
        return new QqListenerWorker(
            settings,
            poller,
            latest,
            delay ?? new ImmediateDelay(),
            new QqListenerDiagnosticsState());
    }

    private static MessageEvent Event(string body) => new(
        "qq.toast",
        "QQ",
        "Alice",
        "",
        body,
        DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
        MessageImportance.Normal,
        ["Alice", body],
        $"qq.toast:{body}");

    private static QqNotificationPollResult Accepted(params MessageEvent[] events) =>
        new(WindowsNotificationReadStatus.Success, events, "success");

    private static QqNotificationPollResult Failed() =>
        new(WindowsNotificationReadStatus.ReadFailed, [], "failed");

    private sealed class BlockingPoller(params MessageEvent[] events) : IQqNotificationPoller
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public int ResetCount { get; private set; }

        public async Task<QqNotificationPollResult> PollAsync(
            bool retryPermission,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await _release.Task;
            return Accepted(events);
        }

        public void ResetDetectionState() => ResetCount++;
        public void Release() => _release.TrySetResult(true);
    }

    private sealed class CancellablePoller : IQqNotificationPoller
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public bool CancellationObserved { get; private set; }

        public async Task<QqNotificationPollResult> PollAsync(
            bool retryPermission,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public void ResetDetectionState()
        {
        }
    }

    private sealed class SequencePoller(params QqNotificationPollResult[] results)
        : IQqNotificationPoller
    {
        private readonly Queue<QqNotificationPollResult> _results = new(results);

        public Task<QqNotificationPollResult> PollAsync(
            bool retryPermission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_results.Dequeue());
        }

        public void ResetDetectionState()
        {
        }
    }

    private sealed class WakePoller : IQqNotificationPoller
    {
        private readonly TaskCompletionSource<bool> _secondPollStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pollCount;

        public Task SecondPollStarted => _secondPollStarted.Task;
        public int ResetCount { get; private set; }

        public async Task<QqNotificationPollResult> PollAsync(
            bool retryPermission,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _pollCount);
            if (count == 1) return Failed();

            _secondPollStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public void ResetDetectionState() => ResetCount++;
    }

    private sealed class ImmediateDelay : IQqListenerDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDelay(
        CancellationTokenSource runCancellation,
        int cancelAfter) : IQqListenerDelay
    {
        public List<TimeSpan> Durations { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Durations.Add(delay);
            if (Durations.Count == cancelAfter)
            {
                runCancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IQqListenerDelay
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public bool CancellationObserved { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
