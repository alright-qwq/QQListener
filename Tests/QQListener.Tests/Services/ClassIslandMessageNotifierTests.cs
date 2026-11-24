using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public class ClassIslandMessageNotifierTests
{
    [Theory]
    [InlineData(MessageImportance.Normal, true, false, false, true)]
    [InlineData(MessageImportance.Important, false, true, false, true)]
    [InlineData(MessageImportance.Calling, false, false, true, true)]
    [InlineData(MessageImportance.Calling, false, false, false, false)]
    [InlineData(MessageImportance.Ignored, true, false, false, false)]
    public void TopmostUsesImportanceSetting(
        MessageImportance importance, bool normal, bool important, bool calling, bool expected)
    {
        var notifier = new ClassIslandMessageNotifier(new QqListenerSettings
        {
            NormalTopmost = normal,
            ImportantTopmost = important,
            CallingTopmost = calling
        });

        Assert.Equal(expected, notifier.GetTopmost(importance));
    }

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
        var longText = new string('测', 50000);

        Assert.Equal(TimeSpan.FromSeconds(1), notifier.GetDuration(CreateMessage(MessageImportance.Normal, longText)));
        Assert.Equal(TimeSpan.FromSeconds(3600), notifier.GetDuration(CreateMessage(MessageImportance.Important, longText)));
        Assert.Equal(TimeSpan.FromSeconds(86400), notifier.GetDuration(CreateMessage(MessageImportance.Calling, longText)));
    }

    [Fact]
    public void MessageDurationScalesWithTextLengthAndStopsAtConfiguredCap()
    {
        var notifier = new ClassIslandMessageNotifier(new QqListenerSettings
        {
            NormalDurationSeconds = 10
        });

        var shortDuration = notifier.GetDuration(CreateMessage(MessageImportance.Normal, "你好"));
        var longDuration = notifier.GetDuration(CreateMessage(MessageImportance.Normal, new string('测', 200)));

        Assert.True(shortDuration < longDuration);
        Assert.True(shortDuration >= TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(10), longDuration);
    }

    [Fact]
    public void CallingKeepsConfiguredDuration()
    {
        var notifier = new ClassIslandMessageNotifier(new QqListenerSettings { CallingDurationSeconds = 600 });

        Assert.Equal(TimeSpan.FromSeconds(600),
            notifier.GetDuration(CreateMessage(MessageImportance.Calling, "呼叫")));
    }

    private static MessageEvent CreateMessage(MessageImportance importance, string body) =>
        new("qq.toast", "QQ", "Alice", "", body, DateTimeOffset.Now, importance, ["Alice", body], "key");

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
