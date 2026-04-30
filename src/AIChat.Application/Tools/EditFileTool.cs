using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class EditFileTool : IAgentTool
{
    public string Id => "edit_file";
    public AgentToolRisk Risk => AgentToolRisk.Write;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "edit_file",
        Description = "修改当前项目内的文本文件。通过 old_text/new_text 做精确替换，默认只替换第一次出现。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["path", "old_text", "new_text"],
          "properties": {
            "path": { "type": "string", "description": "相对项目根目录的文件路径。" },
            "old_text": { "type": "string", "description": "要被替换的原文，必须能在文件中找到。" },
            "new_text": { "type": "string", "description": "替换后的新文本。" },
            "replace_all": { "type": "boolean", "description": "是否替换所有匹配，默认 false。" }
          }
        }
        """
    };

    public async Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var path = ToolJson.GetString(args, "path") ?? "";
        var oldText = ToolJson.GetString(args, "old_text") ?? "";
        var newText = ToolJson.GetString(args, "new_text") ?? "";
        var replaceAll = ToolJson.GetBool(args, "replace_all", defaultValue: false);
        var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
        ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);
        var text = File.Exists(fullPath)
            ? await File.ReadAllTextAsync(fullPath, cancellationToken)
            : "";
        var updated = replaceAll
            ? text.Replace(oldText, newText, StringComparison.Ordinal)
            : ReplaceFirst(text, oldText, newText);

        return new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"修改文件：{path}",
            PreviewText = replaceAll ? "替换全部匹配片段" : "替换单个匹配片段",
            DiffText = ToolDiff.CreateUnifiedDiff(path, text, updated)
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var path = ToolJson.GetString(args, "path");
            var oldText = ToolJson.GetString(args, "old_text");
            var newText = ToolJson.GetString(args, "new_text");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Error("缺少 path 参数。");
            }

            if (string.IsNullOrEmpty(oldText))
            {
                return Error("缺少 old_text 参数，或 old_text 为空。");
            }

            if (newText is null)
            {
                return Error("缺少 new_text 参数。");
            }

            var replaceAll = ToolJson.GetBool(args, "replace_all", defaultValue: false);
            var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
            ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);
            if (!File.Exists(fullPath))
            {
                return Error($"文件不存在：{path}");
            }

            var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var firstIndex = text.IndexOf(oldText, StringComparison.Ordinal);
            if (firstIndex < 0)
            {
                return Error("未找到 old_text，未修改文件。");
            }

            var occurrences = CountOccurrences(text, oldText);
            if (!replaceAll && occurrences > 1)
            {
                return Error($"old_text 出现 {occurrences} 次。为避免误改，请提供更精确片段，或传 replace_all=true。");
            }

            var updated = replaceAll
                ? text.Replace(oldText, newText, StringComparison.Ordinal)
                : ReplaceFirst(text, oldText, newText);
            await File.WriteAllTextAsync(fullPath, updated, cancellationToken);
            return Success(JsonSerializer.Serialize(new
            {
                path = path.Replace('\\', '/'),
                replacements = replaceAll ? occurrences : 1,
                oldChars = text.Length,
                newChars = updated.Length
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string ReplaceFirst(string text, string oldText, string newText)
    {
        var firstIndex = text.IndexOf(oldText, StringComparison.Ordinal);
        return firstIndex < 0
            ? text
            : text.Remove(firstIndex, oldText.Length).Insert(firstIndex, newText);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
