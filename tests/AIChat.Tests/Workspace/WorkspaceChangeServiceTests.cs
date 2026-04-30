using System.Diagnostics;
using AIChat.Application.Workspace;
using AIChat.Tests.Tools;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceChangeServiceTests
{
    [Fact]
    public async Task GetChangesAsync_ReturnsBranchAndChangedFiles()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "tracked.txt"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "new.txt"), "new\n");
        var service = new WorkspaceChangeService();

        var changes = await service.GetChangesAsync(workspace.Path);

        Assert.StartsWith("## ", changes.Branch);
        Assert.Contains(changes.Changes, change => change.Path == "tracked.txt");
        Assert.Contains(changes.Changes, change => change.Path == "new.txt" && change.IsUntracked);
    }

    [Fact]
    public async Task GetDiffAsync_ReturnsPathDiff()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "tracked.txt"), "changed\n");
        var service = new WorkspaceChangeService();

        var diff = await service.GetDiffAsync(workspace.Path, "tracked.txt");

        Assert.Equal("tracked.txt", diff.Path);
        Assert.Contains("-original", diff.DiffText);
        Assert.Contains("+changed", diff.DiffText);
    }

    [Fact]
    public async Task GetDiffAsync_RejectsPathOutsideProject()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var service = new WorkspaceChangeService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetDiffAsync(workspace.Path, "../outside.txt"));

        Assert.Contains("路径超出", ex.Message);
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
