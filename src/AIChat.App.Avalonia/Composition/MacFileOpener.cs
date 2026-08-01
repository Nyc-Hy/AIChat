using System.Diagnostics;

namespace AIChat.App.Avalonia.Composition;

// macOS / Linux opener. Uses the OS's "open" utility (which is
// /usr/bin/open on macOS and whichxdg-open on most Linux
// distros). On Windows we'd want `cmd /c start ""` instead, but
// AIChat's primary platform is macOS and we don't have a Windows
// story yet; the user can plug in a different IFileOpener if
// they need to.
//
// Errors are thrown, not swallowed — the file-tree VM's
// OpenWithSystemApp command catches and surfaces a status
// message so the user knows why nothing happened.
public sealed class MacFileOpener : IFileOpener
{
    public void OpenWithSystemApp(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            throw new ArgumentException("路径不能为空。", nameof(absolutePath));
        }
        if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            throw new FileNotFoundException($"路径不存在：{absolutePath}", absolutePath);
        }

        // `open` on macOS takes the path as a single argument;
        // escaping the path argument is unnecessary because
        // Process.Start with UseShellExecute=true handles it.
        // We don't want a shell window popping up so we use
        // CreateNoWindow=false but on macOS the `open` command
        // is async and returns immediately regardless.
        var startInfo = new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{absolutePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var process = Process.Start(startInfo);
            // `open` exits almost immediately; we don't wait
            // because the user's IDE is the new process, not
            // our process.
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法用系统 app 打开 {absolutePath}：{ex.Message}", ex);
        }
    }
}
