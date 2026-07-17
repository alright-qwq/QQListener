using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public class ClassIslandMessageNotifierTests
{
    [Fact]
    public void DurationIsClampedAtConsumptionPoint()
    {
        var settings = new QqListenerSettings
        {
            NormalDurationSeconds = -100,
            ImportantDurationSeconds = 99999,
            CallingDurationSeconds = 999999
        };
        var notifier = new ClassIslandMessageNotifier(settings);

        Assert.Equal(TimeSpan.FromSeconds(1), notifier.GetDuration(MessageImportance.Normal));
        Assert.Equal(TimeSpan.FromSeconds(3600), notifier.GetDuration(MessageImportance.Important));
        Assert.Equal(TimeSpan.FromSeconds(86400), notifier.GetDuration(MessageImportance.Calling));
    }

    [Fact]
    public async Task ShowAsyncDoesNotInvokeCallbackWhenAlreadyCancelled()
    {
        var notifier = new ClassIslandMessageNotifier(new QqListenerSettings());
        var message = new MessageEvent(
            "qq.toast", "QQ", "Alice", "", "private", DateTimeOffset.Now,
            MessageImportance.Normal, ["Alice", "private"], "key");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var callbackCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            notifier.ShowAsync(message, _ => callbackCount++, cts.Token));

        Assert.Equal(0, callbackCount);
    }
}
