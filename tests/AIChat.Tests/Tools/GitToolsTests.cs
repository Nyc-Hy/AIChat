using System.Diagnostics;
using System.Text.Json;
using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class GitToolsTests
{
    [Fact]
    public async Task GitStatusTool_ReturnsChangedFiles()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "tracked.txt"), "changed");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "new.txt"), "new");
        var tool = new GitStatusTool();

        var result = await tool.ExecuteAsync("{}", new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError, result.Content);
        using var document = JsonDocument.Parse(result.Content);
        var files = document.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Contains(files, file => file.GetProperty("path").GetString() == "tracked.txt");
        Assert.Contains(files, file => file.GetProperty("path").GetString() == "new.txt");
    }

    [Fact]
    public async Task GitDiffTool_ReturnsDiffForChangedFile()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "tracked.txt"), "changed\n");
        var tool = new GitDiffTool();

        var result = await tool.ExecuteAsync(
            """{"path":"tracked.txt"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError, result.Content);
        using var document = JsonDocument.Parse(result.Content);
        var diff = document.RootElement.GetProperty("diff").GetString() ?? "";
        Assert.Contains("-original", diff);
        Assert.Contains("+changed", diff);
    }

    [Fact]
    public async Task GitDiffTool_RejectsPathOutsideProject()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var tool = new GitDiffTool();

        var result = await tool.ExecuteAsync(
            """{"path":"../outside.txt"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("路径超出", result.Content);
    }

    private sealed class GitWorkspace : IDisposable
    {
        private readonly TemporaryWorkspace _workspace;

        private GitWorkspace(TemporaryWorkspace workspace)
        {
            _workspace = workspace;
        }

        public string Path => _workspace.Path;

        public static async Task<GitWorkspace> CreateAsync()
        {
            var workspace = TemporaryWorkspace.Create();
            var result = new GitWorkspace(workspace);
            await RunGitAsync(result.Path, "init");
            await RunGitAsync(result.Path, "config", "user.email", "tests@example.com");
            await RunGitAsync(result.Path, "config", "user.name", "AIChat Tests");
            await File.WriteAllTextAsync(System.IO.Path.Combine(result.Path, "tracked.txt"), "original\n");
            await RunGitAsync(result.Path, "add", "tracked.txt");
            await RunGitAsync(result.Path, "commit", "-m", "initial");
            return result;
        }

        public void Dispose()
        {
            _workspace.Dispose();
        }

        private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
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
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stdout}\n{stderr}");
            }
        }
    }
}
