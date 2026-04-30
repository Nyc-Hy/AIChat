using System.Diagnostics;

namespace AIChat.Application.Tools;

internal static class GitCommand
{
    internal static async Task<GitCommandResult> RunAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var root = ProjectPathGuard.ResolveInsideProject(projectPath, "");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new GitCommandResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask,
                TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new GitCommandResult(-1, "", "git 命令执行超时或被取消。", TimedOut: true);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Timeout cleanup should not hide the command result.
        }
    }
}
