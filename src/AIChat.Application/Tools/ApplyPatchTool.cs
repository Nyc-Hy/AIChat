using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ApplyPatchTool : IAgentTool
{
    public string Id => "apply_patch";
    public AgentToolRisk Risk => AgentToolRisk.Write;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "apply_patch",
        Description = "首选代码修改工具。对当前项目内一个或多个文本文件应用精确补丁。每个操作必须提供 path、old_text、new_text；old_text 必须在文件中唯一匹配。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["changes"],
          "properties": {
            "changes": {
              "type": "array",
              "description": "要应用的文件修改列表。",
              "items": {
                "type": "object",
                "required": ["path", "old_text", "new_text"],
                "properties": {
                  "path": { "type": "string", "description": "相对项目根目录的文件路径。" },
                  "old_text": { "type": "string", "description": "要替换的原文，必须唯一匹配。" },
                  "new_text": { "type": "string", "description": "替换后的文本。" }
                }
              }
            }
          }
        }
        """
    };

    public async Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var changes = ParseChanges(argumentsJson);
        var diffParts = new List<string>();
        foreach (var change in changes)
        {
            var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, change.Path);
            ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);
            var oldContent = File.Exists(fullPath)
                ? await File.ReadAllTextAsync(fullPath, cancellationToken)
                : "";
            var newContent = ApplyChange(oldContent, change, validateUnique: false);
            diffParts.Add(ToolDiff.CreateUnifiedDiff(change.Path, oldContent, newContent));
        }

        return new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"应用 {changes.Count} 个文件补丁",
            PreviewText = JsonSerializer.Serialize(new
            {
                files = changes.Select(change => change.Path.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            }),
            DiffText = string.Join(Environment.NewLine + Environment.NewLine, diffParts)
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var changes = ParseChanges(argumentsJson);
            if (changes.Count == 0)
            {
                return Error("缺少 changes 参数。");
            }

            var plans = new List<PatchPlan>();
            foreach (var change in changes)
            {
                if (string.IsNullOrWhiteSpace(change.Path))
                {
                    return Error("补丁中存在空 path。");
                }

                if (string.IsNullOrEmpty(change.OldText))
                {
                    return Error($"缺少 old_text，或 old_text 为空：{change.Path}");
                }

                if (change.NewText is null)
                {
                    return Error($"缺少 new_text：{change.Path}");
                }

                var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, change.Path);
                ProjectPathGuard.EnsureWritableProjectPath(context.ProjectPath, fullPath);
                if (!File.Exists(fullPath))
                {
                    return Error($"文件不存在：{change.Path}");
                }

                var oldContent = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var newContent = ApplyChange(oldContent, change, validateUnique: true);
                plans.Add(new PatchPlan(change.Path, fullPath, oldContent, newContent));
            }

            foreach (var plan in plans)
            {
                await File.WriteAllTextAsync(plan.FullPath, plan.NewContent, cancellationToken);
            }

            return Success(JsonSerializer.Serialize(new
            {
                changedFiles = plans.Select(plan => new
                {
                    path = plan.Path.Replace('\\', '/'),
                    oldChars = plan.OldContent.Length,
                    newChars = plan.NewContent.Length,
                    contentSnapshot = plan.OldContent,
                    postChangeHash = ToolJson.ComputeSha256(plan.NewContent)
                }).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static IReadOnlyList<PatchChange> ParseChanges(string argumentsJson)
    {
        var root = ToolJson.ParseArguments(argumentsJson);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("changes", out var changesElement) ||
            changesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var changes = new List<PatchChange>();
        foreach (var item in changesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            changes.Add(new PatchChange(
                ToolJson.GetString(item, "path") ?? "",
                ToolJson.GetString(item, "old_text") ?? "",
                ToolJson.GetString(item, "new_text")));
        }

        return changes;
    }

    private static string ApplyChange(string content, PatchChange change, bool validateUnique)
    {
        var occurrences = CountOccurrences(content, change.OldText);
        if (validateUnique && occurrences != 1)
        {
            throw new InvalidOperationException(occurrences == 0
                ? $"未找到 old_text：{change.Path}"
                : $"old_text 在 {change.Path} 中出现 {occurrences} 次。请提供更精确片段。");
        }

        var index = content.IndexOf(change.OldText, StringComparison.Ordinal);
        return index < 0
            ? content
            : content.Remove(index, change.OldText.Length).Insert(index, change.NewText ?? "");
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

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

    private sealed record PatchChange(string Path, string OldText, string? NewText);
    private sealed record PatchPlan(string Path, string FullPath, string OldContent, string NewContent);
}
