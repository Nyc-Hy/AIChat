using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class WriteFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesFileInsideProject()
    {
        using var workspace = TemporaryWorkspace.Create();
        var tool = new WriteFileTool();

        var result = await tool.ExecuteAsync(
            """{"path":"docs/hello.txt","content":"hello","create_directories":true}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "docs", "hello.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOverwriteUnlessExplicit()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "README.md");
        await File.WriteAllTextAsync(target, "old");
        var tool = new WriteFileTool();

        var result = await tool.ExecuteAsync(
            """{"path":"README.md","content":"new"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("overwrite=true", result.Content);
        Assert.Equal("old", await File.ReadAllTextAsync(target));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("obj/generated.txt")]
    [InlineData(".git/config")]
    public async Task ExecuteAsync_RejectsUnsafePaths(string path)
    {
        using var workspace = TemporaryWorkspace.Create();
        var tool = new WriteFileTool();

        var result = await tool.ExecuteAsync(
            $$"""{"path":"{{path}}","content":"nope","overwrite":true}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
    }
}
