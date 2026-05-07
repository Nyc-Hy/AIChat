using AIChat.Application.Artifacts;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ReadInputArtifactTool : IAgentTool
{
    private readonly InputArtifactService _artifactService;

    public ReadInputArtifactTool(InputArtifactService? artifactService = null)
    {
        _artifactService = artifactService ?? new InputArtifactService();
    }

    public string Id => "read_input_artifact";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "read_input_artifact",
        Description = "按 input-artifact:<id> 引用读取用户输入 artifact 的详情。只读，不访问文件系统。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["ref"],
          "properties": {
            "ref": { "type": "string", "description": "输入 artifact 引用，例如 input-artifact:abc123；也可只传 id。" },
            "max_chars": { "type": "integer", "description": "最多返回多少字符，默认 4000。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(
        string argumentsJson,
        AgentToolContext context,
        CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var artifactRef = ToolJson.GetString(args, "ref") ?? "";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"读取输入 artifact：{artifactRef}",
            PreviewText = argumentsJson
        });
    }

    public Task<AgentToolResult> ExecuteAsync(
        string argumentsJson,
        AgentToolContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var artifactRef = ToolJson.GetString(args, "ref");
            if (string.IsNullOrWhiteSpace(artifactRef))
            {
                return Task.FromResult(Error("缺少 ref 参数。"));
            }

            var maxChars = ToolJson.GetInt(args, "max_chars", 4000, 200, 40_000);
            var id = NormalizeRef(artifactRef);
            var artifact = context.InputArtifacts.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.RefId, artifactRef.Trim(), StringComparison.OrdinalIgnoreCase));
            if (artifact is null)
            {
                return Task.FromResult(Error($"未找到输入 artifact：{artifactRef}"));
            }

            var detail = _artifactService.GetDetail(artifact, maxChars);
            if (detail.Length > maxChars)
            {
                detail = detail[..maxChars] + "...";
            }

            return Task.FromResult(Success(detail));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(ex.Message));
        }
    }

    private static string NormalizeRef(string artifactRef)
    {
        var trimmed = artifactRef.Trim();
        const string prefix = "input-artifact:";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
