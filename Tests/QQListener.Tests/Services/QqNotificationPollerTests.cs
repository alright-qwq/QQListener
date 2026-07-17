using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Models;
using QQListener.Services;
using QQListener.Tests.TestSupport;
using Xunit;

namespace QQListener.Tests.Services;

public class QqNotificationPollerTests
{
    [Fact]
    public async Task FailedReadDoesNotForgetVisibleNotifications()
    {
        var a = Snapshot(1, "Alice: one");
        var b = Snapshot(2, "Bob: two");
        var reader = SequencedNotificationReader.Create(
            Success(a),
            Failed(),
            Success(a, b));
        var poller = CreatePoller(reader);

        Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
        Assert.Equal(WindowsNotificationReadStatus.ReadFailed,
            (await poller.PollAsync(false, default)).ReadStatus);
        var recovered = await poller.PollAsync(false, default);

        var accepted = Assert.Single(recovered.AcceptedEvents);
        Assert.Equal("Bob", accepted.Sender);
        Assert.Equal("two", accepted.Body);
    }

    [Fact]
    public async Task SuccessfulEmptyReadCommitsNotificationRemoval()
    {
        var a = Snapshot(1, "Alice: one");
        var reader = SequencedNotificationReader.Create(
            Success(a),
            Success(),
            Success(a));
        var poller = CreatePoller(reader);

        Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
        Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
        var reappeared = await poller.PollAsync(false, default);

        var accepted = Assert.Single(reappeared.AcceptedEvents);
        Assert.Equal("Alice", accepted.Sender);
        Assert.Equal("one", accepted.Body);
    }

    [Fact]
    public async Task ConcurrentPollsNeverReadAtTheSameTime()
    {
        var reader = new BlockingNotificationReader(Success(), Success());
        var poller = CreatePoller(reader);

        var first = poller.PollAsync(false, default);
        await reader.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        var second = poller.PollAsync(false, default);

        Assert.Equal(1, reader.ReadCount);
        reader.ReleaseFirstRead();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, reader.MaxConcurrentReads);
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task WaitingPollCanBeCancelledWithoutEnteringReaderOrBreakingGate()
    {
        var reader = new BlockingNotificationReader(Success(), Success());
        var poller = CreatePoller(reader);

        var first = poller.PollAsync(false, default);
        await reader.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        using var cts = new CancellationTokenSource();
        var waiting = poller.PollAsync(false, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.Equal(1, reader.ReadCount);

        reader.ReleaseFirstRead();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await poller.PollAsync(false, default).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(1, reader.MaxConcurrentReads);
    }

    [Fact]
    public async Task ResetDuringBlockedPollDropsOldResultAndNextPollBaselines()
    {
        var a = Snapshot(1, "Alice: old");
        var b = Snapshot(2, "Bob: baseline");
        var c = Snapshot(3, "Carol: new");
        var reader = new BlockingNotificationReader(
            Success(a),
            Success(b),
            Success(b, c));
        var poller = CreatePoller(reader);

        var stale = poller.PollAsync(false, default);
        await reader.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        poller.ResetDetectionState();
        reader.ReleaseFirstRead();

        Assert.Empty((await stale.WaitAsync(TimeSpan.FromSeconds(2))).AcceptedEvents);
        Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
        var next = await poller.PollAsync(false, default);

        var accepted = Assert.Single(next.AcceptedEvents);
        Assert.Equal("Carol", accepted.Sender);
        Assert.Equal("new", accepted.Body);
    }

    private static QqNotificationPoller CreatePoller(IWindowsNotificationReader reader)
    {
        var settings = new QqListenerSettings();
        var diagnostics = new QqListenerDiagnosticsState();
        var evaluator = new MessageRuleEvaluator(settings);
        var pipeline = new MessageEventPipeline(
            settings,
            evaluator,
            diagnostics,
            NullLogger<MessageEventPipeline>.Instance);

        return new QqNotificationPoller(
            settings,
            reader,
            new QqToastMessageParser(),
            pipeline,
            diagnostics,
            NullLogger<QqNotificationPoller>.Instance);
    }

    private static WindowsToastSnapshot Snapshot(uint id, string text) =>
        new(id, "QQ", "Tencent.QQNT", true, [text]);

    private static WindowsNotificationReadResult Success(params WindowsToastSnapshot[] snapshots) =>
        new(WindowsNotificationReadStatus.Success, snapshots, "success");

    private static WindowsNotificationReadResult Failed() =>
        new(
            WindowsNotificationReadStatus.ReadFailed,
            [],
            "failed",
            new InvalidOperationException("failed"));
}
