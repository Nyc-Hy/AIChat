using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ShellCommandTool : IAgentTool
{
    private const string AutoShell = "auto";
    private const string BashShell = "bash";
    private const string PowerShellShell = "powershell";
    private const string CmdShell = "cmd";

    private static readonly string[] BlockedCommandFragments =
    [
        "Remove-Item", " rm ", " rm-", "del ", "erase ", "rmdir ", " rd ",
        "git reset", "git clean", "format ", "shutdown", "Stop-Computer",
        "mkfs", ":(){", "Set-ExecutionPolicy",
        " -rf ", " -rf/", "-r -f ", "--force", "rm -r",
        "git push.*--force", "git push.*-f ",
        "mv /", "cp /",
        "chmod 777", "chown -R",
        "dd if=", "mkfs.",
        "> /dev/", "shutdown", "reboot", "init 0", "init 6"
    ];

    private static readonly Regex[] BlockedCommandPatterns =
    [
        new(@"\bgit\s+push\b.*\s-f(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bgit\s+push\b.*--force(?:[=\s-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:ri|rd|rmdir)\b.*(?:\s|/|-)r(?:ecurse)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:del|erase)\b.*(?:\s|/)-?s\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    // Commands that are always safe to run without extra confirmation.
    internal static readonly string[] AllowlistedPrefixes =
    [
        "dotnet build", "dotnet test", "dotnet restore", "dotnet run",
        "git status", "git diff", "git log", "git branch", "git show",
        "git remote", "git tag",
        "rg ", "grep ", "find ", "ls ", "dir ", "cat ", "head ", "tail ",
        "wc ", "file ", "stat ", "which ", "where ", "echo ", "pwd",
        "npm list", "npm ls", "npm outdated", "npm test", "npm run build",
        "pytest", "cargo build", "cargo test", "go test",
        "node --version", "dotnet --version", "git --version"
    ];

    private static readonly string[] GitBashCandidates =
    [
        @"C:\Program Files\Git\bin\bash.exe",
        @"C:\Program Files\Git\usr\bin\bash.exe",
        @"C:\Program Files (x86)\Git\bin\bash.exe",
    ];

    public string Id => "run_shell";
    public AgentToolRisk Risk => AgentToolRisk.Shell;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "run_shell",
        Description = "在当前项目目录内运行非交互式 shell 命令。仅作为兜底工具；构建/测试优先用 run_build/run_test，Git 状态/diff 优先用 git_status/git_diff。会阻断常见破坏性命令。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["command"],
          "properties": {
            "command": { "type": "string", "description": "要执行的非交互式命令。例如 dotnet --version、git status、ls -la。" },
            "shell": {
              "type": "string",
              "enum": ["auto", "bash", "powershell", "cmd"],
              "description": "要使用的 shell。默认 auto：Windows 优先 PowerShell，其他系统优先 Bash。"
            },
            "working_directory": { "type": "string", "description": "相对项目根目录的工作目录，留空表示项目根目录。" },
            "timeout_seconds": { "type": "integer", "description": "超时时间，默认 30，最大 120。" },
            "max_output_chars": { "type": "integer", "description": "最多返回多少字符，默认 12000。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var command = ToolJson.GetString(args, "command") ?? "";
        var shell = NormalizeShell(ToolJson.GetString(args, "shell"));
        var workingDirectory = ToolJson.GetString(args, "working_directory") ?? ".";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"运行命令：{command}",
            PreviewText = $"shell={shell}; cwd={workingDirectory}; command={command}"
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var command = ToolJson.GetString(args, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return Error("缺少 command 参数。");
            }

            if (LooksDestructive(command))
            {
                return Error("命令包含常见破坏性片段，已阻断。请改用更安全、可审查的命令。");
            }

            var workingDirectory = ToolJson.GetString(args, "working_directory") ?? "";
            var requestedShell = NormalizeShell(ToolJson.GetString(args, "shell"));
            var timeoutSeconds = ToolJson.GetInt(args, "timeout_seconds", 30, 1, 120);
            var maxOutputChars = ToolJson.GetInt(args, "max_output_chars", 12_000, 1, 40_000);
            var fullWorkingDirectory = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, workingDirectory);
            if (!Directory.Exists(fullWorkingDirectory))
            {
                return Error($"工作目录不存在：{workingDirectory}");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var shell = ResolveShell(requestedShell);
            var tempScriptPath = shell.Kind == PowerShellShell
                ? await CreatePowerShellScriptAsync(command, timeoutCts.Token)
                : null;
            using var process = new Process
            {
                StartInfo = CreateStartInfo(shell, command, fullWorkingDirectory, tempScriptPath),
                EnableRaisingEvents = true
            };

            try
            {
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    KillProcessTree(process);
                    throw;
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var combined = stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : "\n[stderr]\n" + stderr);
                if (string.IsNullOrWhiteSpace(combined))
                {
                    combined = "(命令执行完成，但没有输出。)";
                }

                combined = Truncate(combined, maxOutputChars);
                return Success(JsonSerializer.Serialize(new
                {
                    command,
                    shell = shell.Kind,
                    executable = shell.Executable,
                    workingDirectory = ProjectPathGuard.ToProjectRelativePath(context.ProjectPath, fullWorkingDirectory).Replace('\\', '/'),
                    exitCode = process.ExitCode,
                    timedOut = false,
                    stdout = Truncate(stdout, maxOutputChars),
                    stderr = Truncate(stderr, maxOutputChars),
                    output = combined
                }));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempScriptPath))
                {
                    TryDeleteTempFile(tempScriptPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return Error("命令执行超时或被取消。");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static bool LooksDestructive(string command)
    {
        var padded = " " + command + " ";
        return BlockedCommandFragments.Any(fragment => padded.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
               BlockedCommandPatterns.Any(pattern => pattern.IsMatch(command));
    }

    public static bool IsAllowlisted(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || LooksDestructive(trimmed) || ContainsShellControlOperator(trimmed))
        {
            return false;
        }

        return AllowlistedPrefixes.Any(prefix => MatchesAllowlistedPrefix(trimmed, prefix));
    }

    private static bool MatchesAllowlistedPrefix(string command, string prefix)
    {
        var normalizedPrefix = prefix.Trim();
        return string.Equals(command, normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
               command.StartsWith(normalizedPrefix + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsShellControlOperator(string command)
    {
        return command.Contains(';') ||
               command.Contains('|') ||
               command.Contains('&') ||
               command.Contains('>') ||
               command.Contains('<') ||
               command.Contains('`') ||
               command.Contains("$(", StringComparison.Ordinal);
    }

    private static string NormalizeShell(string? shell)
    {
        if (string.IsNullOrWhiteSpace(shell))
        {
            return AutoShell;
        }

        return shell.Trim().ToLowerInvariant() switch
        {
            BashShell => BashShell,
            PowerShellShell or "pwsh" or "ps" => PowerShellShell,
            CmdShell => CmdShell,
            _ => AutoShell
        };
    }

    private static ShellSpec ResolveShell(string requestedShell)
    {
        if (requestedShell == BashShell)
        {
            return ResolveBash() ?? throw new InvalidOperationException("未找到可用的 bash。请安装 Git for Windows，或将 shell 参数改为 powershell/cmd。");
        }

        if (requestedShell == PowerShellShell)
        {
            return ResolvePowerShell();
        }

        if (requestedShell == CmdShell)
        {
            return new ShellSpec(CmdShell, "cmd.exe");
        }

        return OperatingSystem.IsWindows()
            ? ResolvePowerShell()
            : ResolveBash() ?? ResolvePowerShell();
    }

    private static ShellSpec? ResolveBash()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in GitBashCandidates)
            {
                if (File.Exists(candidate))
                {
                    return new ShellSpec(BashShell, candidate);
                }
            }

            // PATH bash on Windows is often the WSL launcher. It is still useful
            // when explicitly installed, but Git Bash is preferred above.
            return IsExecutableOnPath("bash.exe") ? new ShellSpec(BashShell, "bash.exe") : null;
        }

        return File.Exists("/bin/bash")
            ? new ShellSpec(BashShell, "/bin/bash")
            : new ShellSpec(BashShell, "/bin/sh");
    }

    private static ShellSpec ResolvePowerShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsExecutableOnPath("pwsh.exe")
                ? new ShellSpec(PowerShellShell, "pwsh.exe")
                : new ShellSpec(PowerShellShell, "powershell.exe");
        }

        return new ShellSpec(PowerShellShell, "pwsh");
    }

    private static bool IsExecutableOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        return pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(path => File.Exists(Path.Combine(path, fileName)));
    }

    private static ProcessStartInfo CreateStartInfo(
        ShellSpec shell,
        string command,
        string workingDirectory,
        string? tempScriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shell.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        switch (shell.Kind)
        {
            case BashShell:
                startInfo.ArgumentList.Add("-lc");
                startInfo.ArgumentList.Add(command);
                break;
            case PowerShellShell:
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(tempScriptPath!);
                break;
            case CmdShell:
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
                break;
        }

        return startInfo;
    }

    private static async Task<string> CreatePowerShellScriptAsync(string command, CancellationToken cancellationToken)
    {
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"AIChat-run-shell-{Guid.NewGuid():N}.ps1");
        var script = "$ProgressPreference = 'SilentlyContinue'\n" +
                     "$ErrorActionPreference = 'Continue'\n" +
                     command + "\n" +
                     "if ($null -ne $global:LASTEXITCODE) { exit $global:LASTEXITCODE }\n";
        await File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8, cancellationToken);
        return tempScriptPath;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp cleanup should not hide the actual command result.
        }
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Length <= maxChars
            ? value
            : value[..maxChars] + $"\n...[truncated {value.Length - maxChars} chars]";
    }

    private static void KillProcessTree(Process process)
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
            // Timeout handling should report the timeout, not cleanup failures.
        }
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };

    private sealed record ShellSpec(string Kind, string Executable);
}
