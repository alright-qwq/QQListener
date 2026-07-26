using System.ComponentModel;
using Avalonia;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using QQListener.Models;
using QQListener.Models.ComponentSettings;

namespace QQListener.Components;

[ComponentInfo("5F4F88A9-C0A5-4F11-A1E8-BD8ED0F5D1D6", "最后 QQ 消息", "\uE02B", "显示 QQListener 最近收到的一条 QQ 消息。")]
public partial class LatestQqMessageComponent : ComponentBase<LatestQqMessageComponentSettings>
{
    private LatestMessageState? _latestMessage;

    public static readonly DirectProperty<LatestQqMessageComponent, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, string>(
            nameof(StatusText), o => o.StatusText);

    public static readonly DirectProperty<LatestQqMessageComponent, string> SenderTextProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, string>(
            nameof(SenderText), o => o.SenderText);

    public static readonly DirectProperty<LatestQqMessageComponent, string> MessageTextProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, string>(
            nameof(MessageText), o => o.MessageText);

    public static readonly DirectProperty<LatestQqMessageComponent, bool> IsStatusVisibleProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, bool>(
            nameof(IsStatusVisible), o => o.IsStatusVisible);

    public static readonly DirectProperty<LatestQqMessageComponent, bool> IsSenderVisibleProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, bool>(
            nameof(IsSenderVisible), o => o.IsSenderVisible);

    public static readonly DirectProperty<LatestQqMessageComponent, bool> IsMessageVisibleProperty =
        AvaloniaProperty.RegisterDirect<LatestQqMessageComponent, bool>(
            nameof(IsMessageVisible), o => o.IsMessageVisible);

    private string _statusText = "";
    private string _senderText = "";
    private string _messageText = "";
    private bool _isStatusVisible;
    private bool _isSenderVisible;
    private bool _isMessageVisible;

    public string StatusText
    {
        get => _statusText;
        private set => SetAndRaise(StatusTextProperty, ref _statusText, value);
    }

    public string SenderText
    {
        get => _senderText;
        private set => SetAndRaise(SenderTextProperty, ref _senderText, value);
    }

    public string MessageText
    {
        get => _messageText;
        private set => SetAndRaise(MessageTextProperty, ref _messageText, value);
    }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set => SetAndRaise(IsStatusVisibleProperty, ref _isStatusVisible, value);
    }

    public bool IsSenderVisible
    {
        get => _isSenderVisible;
        private set => SetAndRaise(IsSenderVisibleProperty, ref _isSenderVisible, value);
    }

    public bool IsMessageVisible
    {
        get => _isMessageVisible;
        private set => SetAndRaise(IsMessageVisibleProperty, ref _isMessageVisible, value);
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
            // 设计器/无宿主场景下 IAppHost 可能尚未初始化，GetService 会返回 null。
            // 此处只渲染占位内容，避免 NPE。
            try
            {
                _latestMessage = IAppHost.GetService<LatestMessageState>();
            }
            catch
            {
                _latestMessage = null;
            }
        }
        if (_latestMessage != null)
        {
            _latestMessage.PropertyChanged += OnStateChanged;
        }
        if (Settings != null)
        {
            Settings.PropertyChanged += OnStateChanged;
        }
        UpdateContent();
    }

    private void Visual_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_latestMessage != null)
        {
            _latestMessage.PropertyChanged -= OnStateChanged;
        }
        if (Settings != null)
        {
            Settings.PropertyChanged -= OnStateChanged;
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            UpdateContent();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateContent);
    }

    private void UpdateContent()
    {
        var presentation = LatestQqMessagePresenter.Create(_latestMessage, Settings);
        IsVisible = presentation.IsVisible;
        StatusText = presentation.StatusText;
        SenderText = presentation.SenderText;
        MessageText = presentation.MessageText;
        IsStatusVisible = presentation.IsStatusVisible;
        IsSenderVisible = presentation.IsSenderVisible;
        IsMessageVisible = presentation.IsMessageVisible;
    }

}
