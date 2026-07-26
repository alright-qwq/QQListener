using QQListener.Models;
using QQListener.Models.ComponentSettings;

namespace QQListener.Components;

public sealed record LatestQqMessagePresentation(
    bool IsVisible,
    bool HasDisplayedMessage,
    string StatusText,
    string SenderText,
    string MessageText,
    bool IsStatusVisible,
    bool IsSenderVisible,
    bool IsMessageVisible);

public static class LatestQqMessagePresenter
{
    public static LatestQqMessagePresentation Create(
        LatestMessageState? latestMessage,
        LatestQqMessageComponentSettings? settings)
    {
        settings ??= new LatestQqMessageComponentSettings();
        if (latestMessage is not { HasMessage: true }
            || latestMessage.Importance < settings.MinimumImportance)
        {
            return CreatePlaceholder(settings);
        }

        var statusText = GetStatusText(latestMessage.Importance);
        if (settings.HideSensitiveContent)
        {
            var privacyMessageText = settings.ShowMessageBody
                ? $"{GetSource(latestMessage.Source)} {GetImportanceName(latestMessage.Importance)}消息"
                : "";
            return new LatestQqMessagePresentation(
                true,
                true,
                statusText,
                "",
                privacyMessageText,
                settings.ShowStatusTag && !string.IsNullOrEmpty(statusText),
                false,
                !string.IsNullOrEmpty(privacyMessageText));
        }

        var senderText = latestMessage.Sender;
        var messageText = settings.ShowMessageBody
            ? GetMessageText(latestMessage.Message, senderText, settings.ShowSender)
            : "";
        return new LatestQqMessagePresentation(
            true,
            true,
            statusText,
            senderText,
            messageText,
            settings.ShowStatusTag && !string.IsNullOrEmpty(statusText),
            settings.ShowSender && !string.IsNullOrWhiteSpace(senderText),
            !string.IsNullOrWhiteSpace(messageText));
    }

    private static LatestQqMessagePresentation CreatePlaceholder(LatestQqMessageComponentSettings settings)
    {
        var messageText = settings.ShowPlaceholder ? settings.PlaceholderText : "";
        return new LatestQqMessagePresentation(
            settings.ShowPlaceholder,
            false,
            "",
            "",
            messageText,
            false,
            false,
            settings.ShowPlaceholder && !string.IsNullOrEmpty(messageText));
    }

    private static string GetMessageText(string body, string sender, bool showSender) =>
        !string.IsNullOrWhiteSpace(body)
            ? body
            : showSender ? sender : "";

    private static string GetSource(string source) =>
        string.IsNullOrWhiteSpace(source) ? "QQ" : source;

    private static string GetStatusText(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => "呼叫",
        MessageImportance.Important => "重要",
        _ => ""
    };

    private static string GetImportanceName(MessageImportance importance) => importance switch
    {
        MessageImportance.Calling => "呼叫",
        MessageImportance.Important => "重要",
        _ => "普通"
    };
}
