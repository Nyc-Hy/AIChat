using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class RunTestToolTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsTargetOutsideWorkspace()
    {
        using var workspace = TemporaryWorkspace.Create();
        var tool = new RunTestTool();

        var result = await tool.ExecuteAsync(
            """{"target":"../outside.csproj","timeout_seconds":1}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("路径超出", result.Content);
    }
}
