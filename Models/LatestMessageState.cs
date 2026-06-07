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

    [ObservableProperty]
    private MessageImportance _importance = MessageImportance.Normal;

    [ObservableProperty]
    private string _source = "";

    [ObservableProperty]
    private string _conversation = "";

    public void Update(MessageEvent messageEvent)
    {
        // Set backing fields directly to avoid intermediate PropertyChanged notifications,
        // then fire all notifications at the end for an atomic UI update.
#pragma warning disable MVVMTK0034
        _sender = messageEvent.Sender;
        _message = messageEvent.Body;
        _isImportant = messageEvent.IsImportant;
        _isCalling = messageEvent.IsCalling;
        _importance = messageEvent.Importance;
        _source = messageEvent.SourceDisplayName;
        _conversation = messageEvent.Conversation;
        _receivedAt = messageEvent.ReceivedAt;
        _hasMessage = true;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Sender));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsImportant));
        OnPropertyChanged(nameof(IsCalling));
        OnPropertyChanged(nameof(Importance));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Conversation));
        OnPropertyChanged(nameof(ReceivedAt));
        OnPropertyChanged(nameof(HasMessage));
    }
}
