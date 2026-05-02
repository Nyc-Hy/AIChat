using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ReadFileTool : IAgentTool
{
    public string Id => "read_file";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "read_file",
        Description = "读取当前项目中的一个文本文件。只能读取项目目录内部文件。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["path"],
          "properties": {
            "path": { "type": "string", "description": "相对项目根目录的文件路径。" },
            "max_chars": { "type": "integer", "description": "最多读取多少字符，默认 12000。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var path = ToolJson.GetString(args, "path") ?? "";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"读取文件：{path}",
            PreviewText = argumentsJson
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var path = ToolJson.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Error("缺少 path 参数。");
            }

            var maxChars = ToolJson.GetInt(args, "max_chars", 12_000, 1, 40_000);
            var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
            if (!File.Exists(fullPath))
            {
                return Error($"文件不存在：{path}\n实际查找路径：{fullPath}\n项目根目录：{context.ProjectPath}");
            }

            var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var truncated = text.Length > maxChars;
            if (truncated)
            {
                text = text[..maxChars];
            }

            var payload = JsonSerializer.Serialize(new
            {
                path = path.Replace('\\', '/'),
                chars = text.Length,
                truncated,
                content = text
            });
            return Success(payload);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
