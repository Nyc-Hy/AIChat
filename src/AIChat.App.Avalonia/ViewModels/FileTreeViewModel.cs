using System.Collections.ObjectModel;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// STUB: Wave 2 占位。原来的 FileTreeViewModel / FilePreviewViewModel 在用户
// 工作树里被删了(MainWindowViewModel 还在引用),加这俩 stub 类先让 build 过。
// 真功能(扫描文件树 / 预览内容)由后续 wave 补 — 见 plan §7 Wave 4。
public sealed partial class FileTreeViewModel : ViewModelBase
{
    public event EventHandler<FileSelectedEventArgs>? FileSelected;
    public event EventHandler<string>? StatusMessageRequested;

    [ObservableProperty]
    private ObservableCollection<FileTreeNodeViewModel> root = [];

    [ObservableProperty]
    private bool isBuilding;

    [ObservableProperty]
    private string? error;

    public FileTreeNodeViewModel? SelectedNode { get; set; }

    public void AttachTo(ProjectSidebarViewModel sidebar) { /* noop stub */ }
    public void DetachFrom(ProjectSidebarViewModel sidebar) { /* noop stub */ }
    public Task RebuildAsync(string? projectRoot, CancellationToken token = default) => Task.CompletedTask;
    public Task OpenSelectedAsync() => Task.CompletedTask;
    public void RaiseFileSelected(string relativePath) =>
        FileSelected?.Invoke(this, new FileSelectedEventArgs(relativePath));
    public void RaiseStatusMessage(string message) =>
        StatusMessageRequested?.Invoke(this, message);

    [RelayCommand]
    private void Open() => OpenSelectedAsync().GetAwaiter().GetResult();
}

public sealed class FileTreeNodeViewModel
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = [];
    public bool IsFile { get; init; }
}

public sealed class FileSelectedEventArgs : EventArgs
{
    public string RelativePath { get; }
    public FileSelectedEventArgs(string relativePath) => RelativePath = relativePath;
}
