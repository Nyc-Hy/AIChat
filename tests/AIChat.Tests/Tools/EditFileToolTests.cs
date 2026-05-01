using System.Text.Json;
using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class EditFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_IncludesContentSnapshotAndHash()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(target, "hello world");
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            """{"path":"file.txt","old_text":"world","new_text":"there"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content).RootElement;
        Assert.Equal("hello world", json.GetProperty("contentSnapshot").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("postChangeHash").GetString()));
        Assert.Equal("hello there", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotMatchesOriginalContent()
    {
        using var workspace = TemporaryWorkspace.Create();
        var target = Path.Combine(workspace.Path, "file.txt");
        var original = "line1\nline2\nline3\n";
        await File.WriteAllTextAsync(target, original);
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            """{"path":"file.txt","old_text":"line2","new_text":"LINE2"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content).RootElement;
        Assert.Equal(original, json.GetProperty("contentSnapshot").GetString());
    }
}
