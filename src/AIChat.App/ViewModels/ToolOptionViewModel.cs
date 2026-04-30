namespace AIChat.App.ViewModels;

public sealed class ToolOptionViewModel : ObservableObject
{
    private bool _isEnabled;
    private string _permissionMode = "AutoReadOnly";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskLabel { get; init; }
    public IReadOnlyList<SelectionOptionViewModel> PermissionModeOptions { get; init; } = [];

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string PermissionMode
    {
        get => _permissionMode;
        set => SetProperty(ref _permissionMode, value);
    }
}
