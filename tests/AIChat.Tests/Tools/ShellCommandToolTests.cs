using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class ShellCommandToolTests
{
    [Theory]
    [InlineData("git reset --hard")]
    [InlineData("Remove-Item -Recurse .")]
    public async Task ExecuteAsync_BlocksDestructiveCommands(string command)
    {
        using var workspace = TemporaryWorkspace.Create();
        var tool = new ShellCommandTool();

        var result = await tool.ExecuteAsync(
            $$"""{"command":"{{command}}","shell":"powershell"}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("破坏性", result.Content);
    }
}
