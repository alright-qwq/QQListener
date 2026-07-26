using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models.ComponentSettings;

public partial class LatestQqMessageComponentSettings : ObservableObject
{
    [ObservableProperty]
    private bool _showPlaceholder = true;

    [ObservableProperty]
    private bool _showSender = true;

    [ObservableProperty]
    private bool _showStatusTag = true;

    [ObservableProperty]
    private MessageImportance _minimumImportance = MessageImportance.Normal;

    [ObservableProperty]
    private bool _showMessageBody = true;

    [ObservableProperty]
    private bool _hideSensitiveContent;

    [ObservableProperty]
    private string _placeholderText = "暂无 QQ 消息";
}
