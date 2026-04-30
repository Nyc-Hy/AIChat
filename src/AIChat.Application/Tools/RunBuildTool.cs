using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class RunBuildTool : IAgentTool
{
    public string Id => "run_build";
    public AgentToolRisk Risk => AgentToolRisk.Shell;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "run_build",
        Description = "在当前项目中执行 dotnet build。用于验证代码是否能编译，只允许构建项目内目标。",
        ParametersJson = """
        {
          "type": "object",
          "properties": {
            "target": { "type": "string", "description": "可选，相对项目根目录的 .sln/.csproj 或目录。" },
            "configuration": { "type": "string", "description": "可选，例如 Debug 或 Release。" },
            "timeout_seconds": { "type": "integer", "description": "超时时间，默认 120，最大 600。" },
            "max_output_chars": { "type": "integer", "description": "最多返回多少字符，默认 20000。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var target = ToolJson.GetString(args, "target") ?? ".";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"执行构建：{target}",
            PreviewText = "dotnet build"
        });
    }

    public Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        return DotnetVerification.RunAsync(Id, context.ProjectPath, "build", argumentsJson, cancellationToken);
    }
}
