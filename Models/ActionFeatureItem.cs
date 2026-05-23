using CommunityToolkit.Mvvm.ComponentModel;

namespace QQListener.Models;

public partial class ActionFeatureItem : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string TypeDisplayName => "行动";
    public string? GroupName => "QQListener";
}
