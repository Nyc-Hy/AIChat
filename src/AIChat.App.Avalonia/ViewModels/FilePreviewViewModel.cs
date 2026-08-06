using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// STUB: Wave 2 占位。FilePreviewViewModel 的占位实现。真功能见 plan §7 Wave 4。
public sealed partial class FilePreviewViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? selectedPath;

    [ObservableProperty]
    private string? projectRoot;

    [ObservableProperty]
    private string? content;

    [ObservableProperty]
    private bool hasFile;

    [ObservableProperty]
    private string? error;

    public Task PreviewAsync(string? projectRoot, string? relativePath, CancellationToken token = default)
    {
        ProjectRoot = projectRoot;
        HasFile = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void Clear() => HasFile = false;
}
