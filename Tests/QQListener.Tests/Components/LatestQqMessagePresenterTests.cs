using QQListener.Components;
using QQListener.Models;
using QQListener.Models.ComponentSettings;
using Xunit;

namespace QQListener.Tests.Components;

public sealed class LatestQqMessagePresenterTests
{
    [Theory]
    [InlineData(MessageImportance.Normal, MessageImportance.Important, false)]
    [InlineData(MessageImportance.Important, MessageImportance.Important, true)]
    [InlineData(MessageImportance.Calling, MessageImportance.Important, true)]
    [InlineData(MessageImportance.Important, MessageImportance.Calling, false)]
    public void MinimumImportanceFiltersLatest(
        MessageImportance actual, MessageImportance minimum, bool showsMessage)
    {
        var result = Present(actual, new() { MinimumImportance = minimum });

        Assert.Equal(showsMessage, result.HasDisplayedMessage);
    }

    [Fact]
    public void FilteredMessageUsesPlaceholderWhenConfigured()
    {
        var result = Present(MessageImportance.Normal, new()
        {
            MinimumImportance = MessageImportance.Important,
            ShowPlaceholder = true,
            PlaceholderText = "未达到显示等级"
        });

        Assert.True(result.IsVisible);
        Assert.False(result.HasDisplayedMessage);
        Assert.Equal("未达到显示等级", result.MessageText);
        Assert.True(result.IsMessageVisible);
    }

    [Fact]
    public void FilteredMessageHidesComponentWhenPlaceholderDisabled()
    {
        var result = Present(MessageImportance.Normal, new()
        {
            MinimumImportance = MessageImportance.Important,
            ShowPlaceholder = false
        });

        Assert.False(result.IsVisible);
        Assert.False(result.HasDisplayedMessage);
    }

    [Fact]
    public void PrivacyModeNeverPlacesSecretsInPresentation()
    {
        var result = Present(MessageImportance.Calling, new()
        {
            HideSensitiveContent = true,
            ShowSender = true,
            ShowMessageBody = true
        }, sender: "secret-sender", body: "private-body", conversation: "secret-group");

        var all = string.Join("|", result.StatusText, result.SenderText, result.MessageText);
        Assert.DoesNotContain("secret-sender", all);
        Assert.DoesNotContain("private-body", all);
        Assert.DoesNotContain("secret-group", all);
        Assert.Contains("QQ", all);
        Assert.Contains("呼叫", all);
    }

    [Fact]
    public void PrivacyModeKeepsRedactedPlaceholderWhenMessageBodyIsDisabled()
    {
        var result = Present(MessageImportance.Important, new()
        {
            HideSensitiveContent = true,
            ShowSender = true,
            ShowMessageBody = false
        }, sender: "secret-sender", body: "private-body", conversation: "secret-group");

        Assert.True(result.HasDisplayedMessage);
        Assert.True(result.IsMessageVisible);
        Assert.Equal("QQ 重要消息", result.MessageText);
        Assert.DoesNotContain("secret-sender", result.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-body", result.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-group", result.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public void DisablingMessageBodyLeavesMessageTextEmpty()
    {
        var result = Present(MessageImportance.Normal, new()
        {
            ShowMessageBody = false
        });

        Assert.True(result.HasDisplayedMessage);
        Assert.Equal("", result.MessageText);
        Assert.False(result.IsMessageVisible);
    }

    [Fact]
    public void EmptyBodyDoesNotUseSenderAsMessageWhenSenderIsHidden()
    {
        var result = Present(MessageImportance.Normal, new()
        {
            ShowSender = false,
            ShowMessageBody = true
        }, sender: "secret-sender", body: "");

        Assert.Equal("", result.MessageText);
        Assert.False(result.IsMessageVisible);
    }

    [Fact]
    public void DefaultsExposeNormalMessagesAndBodyWithoutPrivacyMasking()
    {
        var settings = new LatestQqMessageComponentSettings();

        Assert.Equal(MessageImportance.Normal, settings.MinimumImportance);
        Assert.True(settings.ShowMessageBody);
        Assert.False(settings.HideSensitiveContent);
    }

    private static LatestQqMessagePresentation Present(
        MessageImportance importance,
        LatestQqMessageComponentSettings settings,
        string sender = "Alice",
        string body = "hello",
        string conversation = "班群")
    {
        var state = new LatestMessageState();
        state.Update(new MessageEvent(
            "qq.toast", "QQ", sender, conversation, body,
            DateTimeOffset.Parse("2026-07-26T12:00:00+08:00"), importance,
            [sender, body], $"qq.toast:{importance}"));
        return LatestQqMessagePresenter.Create(state, settings);
    }
}
