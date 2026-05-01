using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class WriteFileTool : IAgentTool
{
    public string Id => "write_file";
    public AgentToolRisk Risk => AgentToolRisk.Write;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "write_file",
        Description = "在当前项目内写入文本文件。主要用于创建新文件；修改已有文件时优先使用 apply_patch 或 edit_file。覆盖已有文件必须显式传 overwrite=true。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["path", "content"],
          "properties": {
            "path": { "type": "string", "description": "相对项目根目录的文件路径。" },
            "content": { "type": "string", "description": "要写入的完整文本内容。" },
            "overwrite": { "type": "boolean", "description": "是否允许覆盖已存在文件，默认 false。" },
            "create_directories": { "type": "boolean", "description": "是否自动创建父目录，默认 true。" }
          }
        }
        """
    };

    public async Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var path = ToolJson.GetString(args, "path") ?? "";
        var content = ToolJson.GetString(args, "content") ?? "";
        var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
        ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);
        var existing = File.Exists(fullPath)
            ? await File.ReadAllTextAsync(fullPath, cancellationToken)
            : "";

        return new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = File.Exists(fullPath) ? $"覆盖文件：{path}" : $"创建文件：{path}",
            PreviewText = $"写入 {content.Length} 个字符到 {path.Replace('\\', '/')}",
            DiffText = ToolDiff.CreateUnifiedDiff(path, existing, content)
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var path = ToolJson.GetString(args, "path");
            var content = ToolJson.GetString(args, "content");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Error("缺少 path 参数。");
            }

            if (content is null)
            {
                return Error("缺少 content 参数。");
            }

            var overwrite = ToolJson.GetBool(args, "overwrite", defaultValue: false);
            var createDirectories = ToolJson.GetBool(args, "create_directories", defaultValue: true);
            var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
            ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);

            if (File.Exists(fullPath) && !overwrite)
            {
                return Error($"文件已存在，若要覆盖请传 overwrite=true：{path}");
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                if (!createDirectories)
                {
                    return Error($"父目录不存在：{ProjectPathGuard.ToProjectRelativePath(context.ProjectPath, directory)}");
                }

                Directory.CreateDirectory(directory);
            }

            var existing = File.Exists(fullPath)
                ? await File.ReadAllTextAsync(fullPath, cancellationToken)
                : "";
            await File.WriteAllTextAsync(fullPath, content, cancellationToken);
            return Success(JsonSerializer.Serialize(new
            {
                path = path.Replace('\\', '/'),
                chars = content.Length,
                overwritten = overwrite,
                contentSnapshot = existing,
                postChangeHash = ToolJson.ComputeSha256(content)
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
