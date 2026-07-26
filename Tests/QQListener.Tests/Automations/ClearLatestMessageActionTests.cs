using System.Reflection;
using ClassIsland.Core.Attributes;
using QQListener.Automations.Actions;
using QQListener.Models;
using Xunit;

namespace QQListener.Tests.Automations;

public sealed class ClearLatestMessageActionTests
{
    [Fact]
    public async Task ActionClearsEveryLatestMessageField()
    {
        var state = FilledState();
        var action = new ClearLatestMessageAction(state);
        var method = typeof(ClearLatestMessageAction).GetMethod(
            "OnInvoke", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)method.Invoke(action, null)!;

        Assert.False(state.HasMessage);
        Assert.Equal("", state.Sender);
        Assert.Equal("", state.Message);
    }

    [Fact]
    public void ActionIdIsStable()
    {
        var info = Assert.Single(typeof(ClearLatestMessageAction)
            .GetCustomAttributes(typeof(ActionInfo), false).Cast<ActionInfo>());

        Assert.Equal("QQListener.ClearLatestMessage", info.Id);
    }

    private static LatestMessageState FilledState()
    {
        var state = new LatestMessageState();
        state.Update(new MessageEvent(
            "qq.toast", "QQ", "Alice", "班群", "hello",
            DateTimeOffset.Parse("2026-07-26T12:00:00+08:00"), MessageImportance.Normal,
            ["Alice", "hello"], "qq.toast:test"));
        return state;
    }
}
