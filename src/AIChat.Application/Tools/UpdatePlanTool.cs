using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class UpdatePlanTool : IAgentTool
{
    public string Id => "update_plan";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "update_plan",
        Description = "创建或更新当前任务的执行计划。对多步骤任务，先调用此工具列出计划，再逐步执行；每完成一个步骤，调用此工具更新状态。此工具不会修改项目文件。",
        ParametersJson = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string", "description": "本轮任务计划摘要。" },
            "items": {
              "type": "array",
              "description": "计划项列表。",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "description": "计划项标题。" },
                  "status": { "type": "string", "enum": ["pending", "in_progress", "completed", "blocked", "skipped"], "description": "计划项状态。" },
                  "notes": { "type": "string", "description": "备注。" }
                },
                "required": ["title"]
              }
            }
          },
          "required": ["summary", "items"]
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var summary = ToolJson.GetString(args, "summary") ?? "";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"更新计划：{summary}",
            PreviewText = argumentsJson
        });
    }

    public Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var summary = ToolJson.GetString(args, "summary");
            if (string.IsNullOrWhiteSpace(summary))
            {
                return Task.FromResult(new AgentToolResult
                {
                    ToolName = Id,
                    Content = "缺少必填参数：summary",
                    IsError = true
                });
            }

            if (!args.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(new AgentToolResult
                {
                    ToolName = Id,
                    Content = "缺少必填参数：items（数组）",
                    IsError = true
                });
            }

            var itemCount = items.GetArrayLength();
            var result = JsonSerializer.Serialize(new
            {
                success = true,
                summary,
                itemCount
            });
            return Task.FromResult(new AgentToolResult { ToolName = Id, Content = result });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AgentToolResult
            {
                ToolName = Id,
                Content = ex.Message,
                IsError = true
            });
        }
    }

    public static AgentPlanItemStatus ParseStatus(string status)
    {
        return status?.ToLowerInvariant() switch
        {
            "in_progress" => AgentPlanItemStatus.InProgress,
            "completed" => AgentPlanItemStatus.Completed,
            "blocked" => AgentPlanItemStatus.Blocked,
            "skipped" => AgentPlanItemStatus.Skipped,
            _ => AgentPlanItemStatus.Pending
        };
    }
}
