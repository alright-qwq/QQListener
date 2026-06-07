using System.ComponentModel;
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
        // Set backing fields directly to avoid intermediate PropertyChanged notifications,
        // then fire all notifications at the end for an atomic UI update.
#pragma warning disable MVVMTK0034
        _sender = sender;
        _message = message;
        _isImportant = isImportant;
        _isCalling = isCalling;
        _receivedAt = DateTimeOffset.Now;
        _hasMessage = true;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Sender));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsImportant));
        OnPropertyChanged(nameof(IsCalling));
        OnPropertyChanged(nameof(ReceivedAt));
        OnPropertyChanged(nameof(HasMessage));
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
