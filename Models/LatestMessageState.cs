using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class LatestMessageState : ObservableObject
{
    [ObservableProperty]
    private string _sender = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private DateTimeOffset _receivedAt;

    [ObservableProperty]
    private bool _hasMessage;

    [ObservableProperty]
    private bool _isImportant;

    [ObservableProperty]
    private bool _isCalling;

    public void Update(string sender, string message, bool isImportant, bool isCalling)
    {
        Sender = sender;
        Message = message;
        IsImportant = isImportant;
        IsCalling = isCalling;
        ReceivedAt = DateTimeOffset.Now;
        HasMessage = true;
    }

    public void Clear()
    {
        HasMessage = false;
        IsImportant = false;
        IsCalling = false;
        Sender = "";
        Message = "";
    }
}
