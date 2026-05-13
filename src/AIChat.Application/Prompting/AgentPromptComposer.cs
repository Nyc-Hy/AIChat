using System.Text;
using AIChat.Domain.Chat;

namespace AIChat.Application.Prompting;

public sealed class AgentPromptComposer
{
    private readonly SystemPromptBuilder _systemPromptBuilder;

    public AgentPromptComposer(SystemPromptBuilder? systemPromptBuilder = null)
    {
        _systemPromptBuilder = systemPromptBuilder ?? new SystemPromptBuilder();
    }

    public AgentPromptComposition Compose(AgentPromptComposeRequest request)
    {
        var messages = request.Profile switch
        {
            AgentPromptProfile.Planning => ComposePlanning(request),
            AgentPromptProfile.VerificationRepair => ComposeVerificationRepair(request),
            AgentPromptProfile.Summarization => ComposeSimpleProfile(request, "你是 AIChat 的总结器。输出简洁、可审计的运行结果摘要。"),
            AgentPromptProfile.Review => ComposeSimpleProfile(request, "你是 AIChat 的审阅器。优先指出风险、缺陷、遗漏验证和后续建议。"),
            AgentPromptProfile.ContextGathering => ComposeSimpleProfile(request, "你是 AIChat 的上下文收集 Agent。只选择必要的只读工具和最小上下文。"),
            _ => ComposeExecution(request)
        };

        return new AgentPromptComposition
        {
            Profile = request.Profile,
            Messages = messages,
            EstimatedTokens = EstimateTokens(messages)
        };
    }

    public ChatMessage ComposeExecutionSystemMessage(SystemPromptContext context, string goal = "", AgentStructuredPlan? plan = null)
    {
        return Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Execution,
            Goal = goal,
            ProviderId = context.ProviderId,
            SystemContext = context,
            Plan = plan,
            AllowedTools = context.EnabledToolIds,
            ContextRefs = context.ContextRefs,
            MemorySnippets = context.MemorySnippets,
            InputArtifactRefs = context.InputArtifactRefs
        }).Messages[0];
    }

    private IReadOnlyList<ChatMessage> ComposePlanning(AgentPromptComposeRequest request)
    {
        return
        [
            new ChatMessage
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
                  "subAgents": [
                    {
                      "templateId": "explorer",
                      "phase": "gathering_context",
                      "task": "focused read-only investigation task",
                      "reason": "why this child agent helps",
                      "maxToolCalls": 4,
                      "dependsOn": [],
                      "writeScope": []
                    }
                  ],
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
                - 只有当独立只读调研能明显减少主 Agent 上下文压力时，才在 subAgents 中规划 explorer。
                - 当前只允许 templateId=explorer；不要规划写入型子 Agent。
                - subAgents 最多 2 个，并且必须是可独立完成的聚焦任务。
                - 对代码修改任务必须包含 gathering_context、executing、verifying。
                - suggestedTools 只能从用户当前启用工具中选择。
                """,
                CreatedAt = DateTimeOffset.Now
            },
            new ChatMessage
            {
                Role = ChatRole.User,
                Content = BuildPlanningUserMessage(request),
                CreatedAt = DateTimeOffset.Now
            }
        ];
    }

    private IReadOnlyList<ChatMessage> ComposeExecution(AgentPromptComposeRequest request)
    {
        var system = request.SystemContext is null
            ? "你是 AIChat 的项目 Agent。先理解目标，再谨慎选择工具并验证结果。"
            : _systemPromptBuilder.Build(request.SystemContext);
        system += Environment.NewLine + Environment.NewLine + BuildProfileBlock(request);
        return [new ChatMessage { Role = ChatRole.System, Content = system.Trim(), CreatedAt = DateTimeOffset.Now }];
    }

    private IReadOnlyList<ChatMessage> ComposeVerificationRepair(AgentPromptComposeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是 AIChat 的验证修复 Agent。先阅读失败摘要，定位最小修复点，再运行相关验证。");
        builder.AppendLine("不要扩大修改范围；如果验证信息不足，先读取相关文件或请求更具体的上下文。");
        builder.AppendLine();
        builder.AppendLine(BuildProfileBlock(request));

        return
        [
            new ChatMessage { Role = ChatRole.System, Content = builder.ToString().Trim(), CreatedAt = DateTimeOffset.Now },
            new ChatMessage { Role = ChatRole.User, Content = BuildTaskUserMessage(request), CreatedAt = DateTimeOffset.Now }
        ];
    }

    private IReadOnlyList<ChatMessage> ComposeSimpleProfile(AgentPromptComposeRequest request, string system)
    {
        return
        [
            new ChatMessage
            {
                Role = ChatRole.System,
                Content = (system + Environment.NewLine + Environment.NewLine + BuildProfileBlock(request)).Trim(),
                CreatedAt = DateTimeOffset.Now
            },
            new ChatMessage { Role = ChatRole.User, Content = BuildTaskUserMessage(request), CreatedAt = DateTimeOffset.Now }
        ];
    }

    private static string BuildPlanningUserMessage(AgentPromptComposeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"用户目标：{request.Goal}");
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            builder.AppendLine($"模型：{request.Model}");
        }

        AppendList(builder, "启用工具", request.AllowedTools);
        AppendList(builder, "上下文引用", request.ContextRefs);
        AppendList(builder, "输入 artifact 引用", request.InputArtifactRefs);
        AppendList(builder, "记忆片段", request.MemorySnippets);

        builder.AppendLine();
        builder.AppendLine("最近会话：");
        foreach (var message in request.ConversationMessages.TakeLast(8))
        {
            builder.AppendLine($"[{message.Role}] {Truncate(message.Content.ReplaceLineEndings(" "), 600)}");
        }

        return builder.ToString().Trim();
    }

    private static string BuildTaskUserMessage(AgentPromptComposeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"用户目标：{request.Goal}");
        if (!string.IsNullOrWhiteSpace(request.FailureSummary))
        {
            builder.AppendLine();
            builder.AppendLine("失败摘要：");
            builder.AppendLine(request.FailureSummary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ResponseRequirements))
        {
            builder.AppendLine();
            builder.AppendLine("输出要求：");
            builder.AppendLine(request.ResponseRequirements.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string BuildProfileBlock(AgentPromptComposeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Prompt profile: {request.Profile}");
        if (!string.IsNullOrWhiteSpace(request.Goal))
        {
            builder.AppendLine($"Goal: {request.Goal}");
        }

        if (request.Budget is not null)
        {
            builder.AppendLine($"Budget: tools={request.Budget.MaxToolCalls}, tokens={request.Budget.TokenBudget}");
        }

        if (request.Plan is not null)
        {
            builder.AppendLine($"Plan: {request.Plan.Summary}");
            foreach (var phase in request.Plan.Phases)
            {
                builder.AppendLine($"- {phase.Name}: {phase.Objective}");
            }
        }

        AppendList(builder, "Allowed tools", request.AllowedTools);
        AppendList(builder, "Context refs", request.ContextRefs);
        AppendList(builder, "Input artifact refs", request.InputArtifactRefs);
        AppendList(builder, "Memory snippets", request.MemorySnippets.Select(item => Truncate(item, 300)).ToList());
        return builder.ToString().Trim();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.AppendLine($"{title}:");
        foreach (var value in values.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {value.Trim()}");
        }
    }

    private static string Truncate(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private static int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        var chars = messages.Sum(message =>
            (message.Content?.Length ?? 0) +
            (message.ReasoningContent?.Length ?? 0) +
            message.ToolCalls.Sum(call => call.Name.Length + call.ArgumentsJson.Length));
        return Math.Max(1, (int)Math.Ceiling(chars / 4.0));
    }
}
