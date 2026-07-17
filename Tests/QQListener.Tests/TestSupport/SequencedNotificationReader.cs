using QQListener.Services;

namespace QQListener.Tests.TestSupport;

public sealed class SequencedNotificationReader : IWindowsNotificationReader
{
    private readonly Queue<WindowsNotificationReadResult> _results;

    private SequencedNotificationReader(IEnumerable<WindowsNotificationReadResult> results)
    {
        _results = new Queue<WindowsNotificationReadResult>(results);
    }

    public List<bool> RetryPermissionValues { get; } = [];

    public static SequencedNotificationReader Create(params WindowsNotificationReadResult[] results) =>
        new(results);

    public Task<WindowsNotificationReadResult> ReadToastNotificationsAsync(
        bool retryPermission,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RetryPermissionValues.Add(retryPermission);
        return Task.FromResult(_results.Dequeue());
    }
}

public sealed class BlockingNotificationReader : IWindowsNotificationReader
{
    private readonly Queue<WindowsNotificationReadResult> _results;
    private readonly TaskCompletionSource<bool> _firstReadStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseFirstRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeReads;
    private int _maxConcurrentReads;
    private int _readCount;

    public BlockingNotificationReader(params WindowsNotificationReadResult[] results)
    {
        _results = new Queue<WindowsNotificationReadResult>(results);
    }

    public Task FirstReadStarted => _firstReadStarted.Task;

    public int ReadCount => Volatile.Read(ref _readCount);

    public int MaxConcurrentReads => Volatile.Read(ref _maxConcurrentReads);

    public void ReleaseFirstRead() => _releaseFirstRead.TrySetResult(true);

    public async Task<WindowsNotificationReadResult> ReadToastNotificationsAsync(
        bool retryPermission,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readNumber = Interlocked.Increment(ref _readCount);
        var concurrent = Interlocked.Increment(ref _activeReads);
        UpdateMaximum(concurrent);

        try
        {
            if (readNumber == 1)
            {
                _firstReadStarted.TrySetResult(true);
                await _releaseFirstRead.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return _results.Dequeue();
        }
        finally
        {
            Interlocked.Decrement(ref _activeReads);
        }
    }

    private void UpdateMaximum(int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maxConcurrentReads);
            if (current >= value) return;
            if (Interlocked.CompareExchange(ref _maxConcurrentReads, value, current) == current) return;
        }
    }
}
