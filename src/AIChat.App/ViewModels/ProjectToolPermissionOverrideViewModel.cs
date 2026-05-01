namespace AIChat.App.ViewModels;

public sealed class ProjectToolPermissionOverrideViewModel : ObservableObject
{
    private string _toolId = "";
    private string _permissionMode = "ConfirmEachTime";

    public required string ToolId
    {
        get => _toolId;
        set => SetProperty(ref _toolId, value);
    }

    public required string PermissionMode
    {
        get => _permissionMode;
        set => SetProperty(ref _permissionMode, value);
    }

    public IReadOnlyList<SelectionOptionViewModel> PermissionModeOptions { get; init; } = [];
}
