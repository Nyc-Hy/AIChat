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

    [Fact]
    public async Task GitRestoreFileTool_RestoresTrackedFile()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var targetPath = Path.Combine(workspace.Path, "tracked.txt");
        await File.WriteAllTextAsync(targetPath, "changed\n");
        var tool = new GitRestoreFileTool();

        var preview = await tool.PreviewAsync(
            """{"path":"tracked.txt"}""",
            new AgentToolContext { ProjectPath = workspace.Path });
        var result = await tool.ExecuteAsync(
            """{"path":"tracked.txt"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.Contains("-original", preview.DiffText);
        Assert.Contains("+changed", preview.DiffText);
        Assert.False(result.IsError, result.Content);
        Assert.Equal("original\n", (await File.ReadAllTextAsync(targetPath)).ReplaceLineEndings("\n"));
        using var document = JsonDocument.Parse(result.Content);
        Assert.True(document.RootElement.GetProperty("restored").GetBoolean());
        Assert.Equal("tracked.txt", document.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task GitRestoreFileTool_RequiresExplicitDeleteForUntrackedFile()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var targetPath = Path.Combine(workspace.Path, "new.txt");
        await File.WriteAllTextAsync(targetPath, "new\n");
        var tool = new GitRestoreFileTool();

        var result = await tool.ExecuteAsync(
            """{"path":"new.txt"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("delete_untracked=true", result.Content);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task GitRestoreFileTool_DeletesUntrackedFileWhenExplicitlyAllowed()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var targetPath = Path.Combine(workspace.Path, "new.txt");
        await File.WriteAllTextAsync(targetPath, "new\n");
        var tool = new GitRestoreFileTool();

        var preview = await tool.PreviewAsync(
            """{"path":"new.txt","delete_untracked":true}""",
            new AgentToolContext { ProjectPath = workspace.Path });
        var result = await tool.ExecuteAsync(
            """{"path":"new.txt","delete_untracked":true}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.Contains("-new", preview.DiffText);
        Assert.False(result.IsError, result.Content);
        Assert.False(File.Exists(targetPath));
        using var document = JsonDocument.Parse(result.Content);
        Assert.True(document.RootElement.GetProperty("deletedUntracked").GetBoolean());
    }

    [Fact]
    public async Task GitCommitTool_CommitsExplicitPaths()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "tracked.txt"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "ignored.txt"), "left out\n");
        var tool = new GitCommitTool();

        var result = await tool.ExecuteAsync(
            """{"message":"Update tracked file","paths":["tracked.txt"]}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError, result.Content);
        using var document = JsonDocument.Parse(result.Content);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("commit").GetString()));

        var status = await RunGitCaptureAsync(workspace.Path, "status", "--short", "--untracked-files=all");
        Assert.DoesNotContain("tracked.txt", status);
        Assert.Contains("ignored.txt", status);
    }

    [Fact]
    public async Task GitCommitTool_RejectsMissingPaths()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var tool = new GitCommitTool();

        var result = await tool.ExecuteAsync(
            """{"message":"No paths","paths":[]}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("paths", result.Content);
    }

    [Fact]
    public async Task GitCommitTool_RejectsPathOutsideProject()
    {
        using var workspace = await GitWorkspace.CreateAsync();
        var tool = new GitCommitTool();

        var result = await tool.ExecuteAsync(
            """{"message":"Bad path","paths":["../outside.txt"]}""",
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
            _ = await RunGitCaptureAsync(workingDirectory, arguments);
        }
    }

    private static async Task<string> RunGitCaptureAsync(string workingDirectory, params string[] arguments)
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

        return stdout;
    }
}
