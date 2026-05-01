using System.Text.Json;
using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class ApplyPatchToolTests
{
    [Fact]
    public async Task ExecuteAsync_AppliesSinglePreciseChange()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(target, "alpha\nbeta\ngamma\n");
        var tool = new ApplyPatchTool();

        var result = await tool.ExecuteAsync(
            """
            {
              "changes": [
                { "path": "file.txt", "old_text": "beta", "new_text": "BETA" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError);
        Assert.Equal("alpha\nBETA\ngamma\n", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_IsAtomicWhenLaterChangeFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        var first = Path.Combine(workspace.Path, "first.txt");
        var second = Path.Combine(workspace.Path, "second.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        var tool = new ApplyPatchTool();

        var result = await tool.ExecuteAsync(
            """
            {
              "changes": [
                { "path": "first.txt", "old_text": "one", "new_text": "ONE" },
                { "path": "second.txt", "old_text": "missing", "new_text": "TWO" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Equal("one", await File.ReadAllTextAsync(first));
        Assert.Equal("two", await File.ReadAllTextAsync(second));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAmbiguousOldText()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(target, "same\nsame\n");
        var tool = new ApplyPatchTool();

        var result = await tool.ExecuteAsync(
            """
            {
              "changes": [
                { "path": "file.txt", "old_text": "same", "new_text": "changed" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("出现 2 次", result.Content);
        Assert.Equal("same\nsame\n", await File.ReadAllTextAsync(target));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("bin/generated.txt")]
    [InlineData(".git/config")]
    public async Task ExecuteAsync_RejectsUnsafePaths(string path)
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "safe.txt");
        await File.WriteAllTextAsync(target, "safe");
        var tool = new ApplyPatchTool();

        var result = await tool.ExecuteAsync(
            $$"""
            {
              "changes": [
                { "path": "{{path}}", "old_text": "safe", "new_text": "unsafe" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesContentSnapshotAndHash()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(target, "alpha\nbeta\ngamma\n");
        var tool = new ApplyPatchTool();

        var result = await tool.ExecuteAsync(
            """
            {
              "changes": [
                { "path": "file.txt", "old_text": "beta", "new_text": "BETA" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content).RootElement;
        var changedFile = json.GetProperty("changedFiles")[0];
        Assert.Equal("alpha\nbeta\ngamma\n", changedFile.GetProperty("contentSnapshot").GetString());
        Assert.False(string.IsNullOrEmpty(changedFile.GetProperty("postChangeHash").GetString()));
    }

    [Fact]
    public async Task PreviewAsync_ReturnsDiffWithoutWritingFile()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(target, "before");
        var tool = new ApplyPatchTool();

        var preview = await tool.PreviewAsync(
            """
            {
              "changes": [
                { "path": "file.txt", "old_text": "before", "new_text": "after" }
              ]
            }
            """,
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.Contains("-before", preview.DiffText);
        Assert.Contains("+after", preview.DiffText);
        Assert.Equal("before", await File.ReadAllTextAsync(target));
    }
}
