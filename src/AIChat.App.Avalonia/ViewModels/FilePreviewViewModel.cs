using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Read-only preview of a single file. The host (MainWindow) drives
// PreviewAsync when the user picks a leaf in FileTreeViewModel;
// this VM reads the bytes off the UI thread, splits them into
// lines, and exposes Lines for the XAML. Closing the preview
// wipes ContentPath so the XAML's IsVisible flips back to false
// and the activity feed takes over the full conversation panel.
//
// Not a full editor — no syntax highlighting, no save. The
// double-click → system-open path (Phase 1.4) is the affordance
// for "I want to edit this file", and that uses the user's
// actual IDE rather than a half-baked in-app editor.
public sealed partial class FilePreviewViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _currentLoadCts;
    private bool _disposed;

    [ObservableProperty]
    private string contentPath = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string projectRoot = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? loadError;

    [ObservableProperty]
    private long fileSize;

    // Lines is the source of truth for the XAML's ItemsControl
    // (one TextBlock per line, with the line number as a small
    // left gutter). We split the content here instead of
    // showing one giant TextBlock so very long lines (minified
    // JSON, generated code) don't crush the XAML measurement
    // pass and the scroll-to-line affordance we want later has
    // a row to land on.
    public ObservableCollection<FilePreviewLine> Lines { get; } = new();

    public bool HasFile => !string.IsNullOrEmpty(ContentPath);

    public async Task PreviewAsync(string? projectRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            Clear();
            return;
        }

        // Resolve the path inside the project root. We deliberately
        // don't allow absolute paths from outside the project — the
        // tree only feeds in RelativePath values, and the host
        // passes projectRoot so we can defend against path
        // traversal even if a leaf's RelativePath gets weird.
        var fullPath = ResolveInsideProject(projectRoot, relativePath);

        _currentLoadCts?.Cancel();
        _currentLoadCts = new CancellationTokenSource();
        var token = _currentLoadCts.Token;

        IsLoading = true;
        LoadError = null;
        ProjectRoot = projectRoot ?? "";

        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                throw new FileNotFoundException($"文件不存在：{relativePath}", fullPath);
            }
            // 2 MB soft cap. A real file viewer would do something
            // smarter (chunked read, virtual scroll, etc.); for the
            // MVP "let me see what's in this file" use case, refusing
            // a 50 MB log with a clear message is better than hanging
            // the UI.
            if (info.Length > 2_000_000)
            {
                throw new InvalidOperationException(
                    $"文件过大 ({info.Length:N0} 字节)，无法预览。请用系统 app 打开。");
            }

            var content = await Task.Run(() => File.ReadAllTextAsync(fullPath, token), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            ContentPath = fullPath;
            DisplayName = Path.GetFileName(fullPath);
            FileSize = info.Length;
            Lines.Clear();
            foreach (var (line, index) in SplitLines(content))
            {
                Lines.Add(new FilePreviewLine(index + 1, line));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer preview superseded this one — silently drop.
            // Do NOT touch ContentPath: leaving the previous successful
            // value in place keeps HasFile true so the panel stays
            // visible. The next load will overwrite both fields when
            // it completes (success or error).
        }
        catch (Exception ex)
        {
            // Error path: surface the message but DO NOT set
            // ContentPath. Pre-fix, the catch block set ContentPath =
            // fullPath so HasFile=true and the panel stayed visible —
            // but the only visible body was "正在读取文件…" (because
            // IsLoading had been reset to false) or the close-X in a
            // panel that pointed at a non-existent file. Clearing the
            // path makes the panel collapse (HasFile=false) so the
            // activity feed takes over the full conversation area.
            // The user can pick the file again to retry.
            LoadError = ex.Message;
            Lines.Clear();
            ContentPath = "";
            DisplayName = "";
            FileSize = 0;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void Clear()
    {
        ContentPath = "";
        DisplayName = "";
        ProjectRoot = "";
        FileSize = 0;
        Lines.Clear();
        LoadError = null;
    }

    // Splits content into lines without producing a trailing
    // empty entry for a file that ends with a newline. Returns
    // (line, 0-based index) pairs so the caller can turn the
    // index into a 1-based line number for the gutter.
    private static IEnumerable<(string line, int index)> SplitLines(string content)
    {
        var index = 0;
        var current = new System.Text.StringBuilder();
        foreach (var ch in content)
        {
            if (ch == '\n')
            {
                yield return (current.ToString(), index++);
                current.Clear();
            }
            else if (ch != '\r')
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0)
        {
            yield return (current.ToString(), index);
        }
    }

    // Resolve a relative path against the project root and
    // make sure the result is still inside the project.
    // (Defends against the (unlikely but possible) case where a
    // tree entry has a relative path that tries to escape via
    // "..".)
    private static string ResolveInsideProject(string? projectRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return relativePath;
        }
        var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var normalizedRoot = Path.GetFullPath(projectRoot);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"拒绝预览项目外的路径：{relativePath}");
        }
        return fullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _currentLoadCts?.Cancel();
        _currentLoadCts?.Dispose();
    }
}

public sealed record FilePreviewLine(int Number, string Text);
