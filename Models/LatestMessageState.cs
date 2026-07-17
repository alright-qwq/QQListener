using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class LatestMessageState : ObservableObject
{
    private readonly object _stateLock = new();

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
        lock (_stateLock)
        {
            _sender = messageEvent.Sender;
            _message = messageEvent.Body;
            _isImportant = messageEvent.IsImportant;
            _isCalling = messageEvent.IsCalling;
            _importance = messageEvent.Importance;
            _source = messageEvent.SourceDisplayName;
            _conversation = messageEvent.Conversation;
            _receivedAt = messageEvent.ReceivedAt;
            _hasMessage = true;
        }
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

#pragma warning disable MVVMTK0034
    public bool TryCreateReplayEvent(out MessageEvent messageEvent)
    {
        lock (_stateLock)
        {
            if (!_hasMessage)
            {
                messageEvent = null!;
                return false;
            }

            messageEvent = new MessageEvent(
                "qq.toast",
                string.IsNullOrWhiteSpace(_source) ? "QQ" : _source,
                _sender,
                _conversation,
                _message,
                _receivedAt,
                _importance,
                [_sender, _message],
                "qq.toast:replay");
            return true;
        }
    }
#pragma warning restore MVVMTK0034

    public void Clear()
    {
#pragma warning disable MVVMTK0034
        lock (_stateLock)
        {
            _sender = "";
            _message = "";
            _conversation = "";
            _source = "";
            _receivedAt = default;
            _hasMessage = false;
            _isImportant = false;
            _isCalling = false;
            _importance = MessageImportance.Normal;
        }
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
