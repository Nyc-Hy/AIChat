using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ListFilesTool : IAgentTool
{
    public string Id => "list_files";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "list_files",
        Description = "列出当前项目中的文件。用于了解项目结构，默认跳过 bin、obj、.vs、artifacts 等生成目录。",
        ParametersJson = """
        {
          "type": "object",
          "properties": {
            "directory": { "type": "string", "description": "相对项目根目录的文件夹路径，留空表示项目根目录。" },
            "max_results": { "type": "integer", "description": "最多返回多少个文件，默认 80。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var directory = ToolJson.GetString(args, "directory") ?? ".";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"列出目录：{directory}",
            PreviewText = argumentsJson
        });
    }

    public Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var directory = ToolJson.GetString(args, "directory") ?? "";
            var maxResults = ToolJson.GetInt(args, "max_results", 80, 1, 300);
            var fullDirectory = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, directory);
            if (!Directory.Exists(fullDirectory))
            {
                return Task.FromResult(Error($"目录不存在：{directory}"));
            }

            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".vs", "bin", "obj", "artifacts", "TestResults"
            };
            var files = Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(skip.Contains))
                .Take(maxResults)
                .Select(path => ProjectPathGuard.ToProjectRelativePath(context.ProjectPath, path).Replace('\\', '/'))
                .ToList();

            var payload = JsonSerializer.Serialize(new
            {
                directory,
                count = files.Count,
                files
            });
            return Task.FromResult(Success(payload));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(ex.Message));
        }
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
