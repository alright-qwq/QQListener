using System.ComponentModel;
using Avalonia;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using QQListener.Models;
using QQListener.Models.ComponentSettings;

namespace QQListener.Components;

[ComponentInfo("5F4F88A9-C0A5-4F11-A1E8-BD8ED0F5D1D6", "最后 QQ 消息", "\uE715", "显示 QQListener 最近收到的一条 QQ 消息。")]
public partial class LatestQqMessageComponent : ComponentBase<LatestQqMessageComponentSettings>, INotifyPropertyChanged
{
    private LatestMessageState? _latestMessage;
    private string _statusText = "";
    private string _senderText = "";
    private string _messageText = "";
    private bool _isStatusVisible;
    private bool _isSenderVisible;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value, nameof(StatusText));
    }

    public string SenderText
    {
        get => _senderText;
        private set => SetField(ref _senderText, value, nameof(SenderText));
    }

    public string MessageText
    {
        get => _messageText;
        private set => SetField(ref _messageText, value, nameof(MessageText));
    }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set => SetField(ref _isStatusVisible, value, nameof(IsStatusVisible));
    }

    public bool IsSenderVisible
    {
        get => _isSenderVisible;
        private set => SetField(ref _isSenderVisible, value, nameof(IsSenderVisible));
    }

    public LatestQqMessageComponent()
    {
        InitializeComponent();
    }

    public LatestQqMessageComponent(LatestMessageState latestMessage) : this()
    {
        _latestMessage = latestMessage;
    }

    private void Visual_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_latestMessage == null)
        {
            _latestMessage = IAppHost.GetService<LatestMessageState>();
        }
        _latestMessage.PropertyChanged += OnStateChanged;
        Settings.PropertyChanged += OnStateChanged;
        UpdateContent();
    }

    private void Visual_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_latestMessage != null)
        {
            _latestMessage.PropertyChanged -= OnStateChanged;
        }
        Settings.PropertyChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateContent();
    }

    private void UpdateContent()
    {
        if (_latestMessage is not { HasMessage: true })
        {
            IsVisible = Settings.ShowPlaceholder;
            StatusText = "";
            SenderText = "";
            MessageText = Settings.PlaceholderText;
            IsStatusVisible = false;
            IsSenderVisible = false;
            return;
        }

        IsVisible = true;
        StatusText = _latestMessage.IsCalling ? "呼叫" : _latestMessage.IsImportant ? "重要" : "";
        SenderText = _latestMessage.Sender;
        MessageText = string.IsNullOrWhiteSpace(_latestMessage.Message) ? _latestMessage.Sender : _latestMessage.Message;
        IsStatusVisible = Settings.ShowStatusTag && !string.IsNullOrEmpty(StatusText);
        IsSenderVisible = Settings.ShowSender && !string.IsNullOrWhiteSpace(SenderText);
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
