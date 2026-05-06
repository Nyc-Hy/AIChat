using System.Text;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Planning;

public sealed class PlannerPromptBuilder
{
    public IReadOnlyList<ChatMessage> Build(AgentPlanningRequest request)
    {
        var system = new ChatMessage
        {
            Role = ChatRole.System,
            Content = """
            你是 AIChat 的结构化规划器。只输出 JSON，不要输出 Markdown。
            你不执行工具、不承诺已经完成任务，只规划当前单个 Agent 应该如何执行。

            JSON schema:
            {
              "summary": "short plan summary",
              "suggestedTools": ["read_file"],
              "suggestedContext": ["files or concepts to inspect"],
              "budget": { "maxToolCalls": 8, "tokenBudget": 12000, "notes": "" },
              "phases": [
                {
                  "name": "planning|gathering_context|executing|verifying|summarizing",
                  "objective": "phase goal",
                  "tasks": [
                    {
                      "title": "task title",
                      "details": "what to do",
                      "risk": "low|medium|high",
                      "suggestedTools": ["read_file"],
                      "suggestedContext": ["relevant context"],
                      "budget": { "maxToolCalls": 2, "tokenBudget": 3000, "notes": "" }
                    }
                  ]
                }
              ]
            }

            规则：
            - 至少返回一个 phase 和一个 task。
            - 总任务数保持精简，默认 3-7 个。
            - 不要创建多个 agent，不要描述并行执行。
            - 对代码修改任务必须包含 gathering_context、executing、verifying。
            - suggestedTools 只能从用户当前启用工具中选择。
            """,
            CreatedAt = DateTimeOffset.Now
        };

        var userBuilder = new StringBuilder();
        userBuilder.AppendLine($"用户目标：{request.Goal}");
        userBuilder.AppendLine($"项目路径：{request.ProjectPath}");
        userBuilder.AppendLine("启用工具：");
        foreach (var tool in request.EnabledToolIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            userBuilder.AppendLine($"- {tool}");
        }

        userBuilder.AppendLine();
        userBuilder.AppendLine("最近会话：");
        foreach (var message in request.Messages.TakeLast(8))
        {
            var content = message.Content.ReplaceLineEndings(" ").Trim();
            if (content.Length > 600)
            {
                content = content[..600] + "...";
            }

            userBuilder.AppendLine($"[{message.Role}] {content}");
        }

        return
        [
            system,
            new ChatMessage
            {
                Role = ChatRole.User,
                Content = userBuilder.ToString().Trim(),
                CreatedAt = DateTimeOffset.Now
            }
        ];
    }
}
