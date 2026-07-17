using QQListener.Models;
using Xunit;

namespace QQListener.Tests.Models;

public class LatestMessageStateTests
{
    [Fact]
    public void ClearRemovesEveryCachedMessageField()
    {
        var state = new LatestMessageState();
        state.Update(new MessageEvent(
            "qq.toast",
            "QQ",
            "Alice",
            "Class Group",
            "private-body",
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            MessageImportance.Calling,
            ["Alice", "private-body"],
            "key"));

        state.Clear();

        Assert.False(state.HasMessage);
        Assert.Equal("", state.Sender);
        Assert.Equal("", state.Message);
        Assert.Equal("", state.Conversation);
        Assert.Equal("", state.Source);
        Assert.Equal(default, state.ReceivedAt);
        Assert.False(state.IsImportant);
        Assert.False(state.IsCalling);
        Assert.Equal(MessageImportance.Normal, state.Importance);
    }
}
