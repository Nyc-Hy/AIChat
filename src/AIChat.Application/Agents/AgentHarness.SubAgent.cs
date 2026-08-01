using AIChat.Application.Agents.Planning;
using AIChat.Application.Agents.SubAgents;
using AIChat.Application.Security;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Sub-agent + structured-plan formatters — split out from the
// main AgentHarness partial so the orchestration file stays
// focused on the run loop. These helpers all live in service of
// formatting the human-readable output that goes into agent
// step / sub-agent result / context-summary slots:
// CreateContextStepOutput renders the gathering-context phase
// output (used by RunContextPhaseAsync), CreateStructuredPlan
// StepOutput renders the planning phase output (used by
// RunPlanPhaseAsync), and the rest of the formatters render
// the per-sub-agent task / result / appended-message shapes
// used by RunSubAgentPhaseAsync. ComputeSubAgentExecution
// Layers stays here too (it's a public static, called from
// SubAgentLayerSchedulerTests).
public sealed partial class AgentHarness
{
    private static string CreateContextStepOutput(AgentRun run, AgentTaskExecutionPolicy policy)
    {
        var lines = new List<string>
        {
            "已生成系统提示和会话上下文。",
            $"项目：{(string.IsNullOrWhiteSpace(run.ProjectPath) ? "未记录" : run.ProjectPath)}",
            $"模型：{(string.IsNullOrWhiteSpace(run.Model) ? "未记录" : run.Model)}",
            $"工具：{(run.EnabledTools.Count == 0 ? "无" : string.Join(", ", run.EnabledTools))}",
            $"预算：最多 {run.MaxToolRounds} 轮工具调用",
            $"工作区：{(string.IsNullOrWhiteSpace(run.WorkspaceBranch) ? "未记录分支" : run.WorkspaceBranch)} · {run.WorkspaceChangeCountAtStart} 个启动变更"
        };

        if (run.WorkspaceChangesWereTruncated)
        {
            lines[^1] += "（列表被截断）";
        }

        if (!string.IsNullOrWhiteSpace(run.ContinuedFromRunId))
        {
            lines.Add($"继续运行：{run.ContinuedFromRunId}");
        }

        if (!string.IsNullOrWhiteSpace(run.RetriedFromRunId))
        {
            lines.Add($"重试来源：{run.RetriedFromRunId}");
        }

        var preferences = AgentExecutionPolicySummaryBuilder.FormatPreferences(policy);
        if (!string.IsNullOrWhiteSpace(preferences))
        {
            lines.Add($"历史偏好：{preferences}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateStructuredPlanStepOutput(AgentStructuredPlan plan)
    {
        var lines = new List<string>
        {
            $"摘要：{plan.Summary}",
            $"来源：{(plan.IsFallback ? "兜底计划" : "LLM 结构化规划")}",
            $"预算：工具 {plan.Budget.MaxToolCalls} 次，tokens {plan.Budget.TokenBudget}",
            $"阶段：{plan.Phases.Count}",
            $"任务：{plan.Phases.Sum(phase => phase.Tasks.Count)}",
            $"计划子 Agent：{plan.SubAgents.Count}"
        };

        foreach (var phase in plan.Phases)
        {
            lines.Add($"- {phase.Name}: {phase.Objective}");
            foreach (var task in phase.Tasks)
            {
                lines.Add($"  - {task.Title} ({task.Risk})");
            }
        }

        if (plan.SubAgents.Count > 0)
        {
            lines.Add("子 Agent 计划：");
            foreach (var agent in plan.SubAgents.OrderBy(agent => agent.Order))
            {
                lines.Add($"- {agent.TemplateId}: {agent.Task} ({agent.Phase}, tools {agent.MaxToolCalls})");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSubAgentTask(AgentSubAgentScheduleDecision plannedSubAgent, AgentRun run, string goal)
    {
        if (!string.IsNullOrWhiteSpace(plannedSubAgent.Task))
        {
            return string.IsNullOrWhiteSpace(plannedSubAgent.Reason)
                ? plannedSubAgent.Task
                : $"{plannedSubAgent.Task}\nReason: {plannedSubAgent.Reason}";
        }

        return BuildFallbackExplorerTask(run, goal);
    }

    private static string BuildFallbackExplorerTask(AgentRun run, string goal)
    {
        var task = run.StructuredPlan?.Phases
            .FirstOrDefault(phase => phase.Name.Contains("gather", StringComparison.OrdinalIgnoreCase) ||
                                     phase.Name.Contains("context", StringComparison.OrdinalIgnoreCase))
            ?.Tasks.FirstOrDefault();
        if (task is not null)
        {
            return string.IsNullOrWhiteSpace(task.Details)
                ? task.Title
                : $"{task.Title}: {task.Details}";
        }

        return $"Gather the minimum read-only context needed for: {goal}";
    }

    private static string FormatSubAgentResult(SubAgentRun subAgentRun)
    {
        var result = subAgentRun.Result;
        if (result is null)
        {
            return $"{subAgentRun.TemplateId} {subAgentRun.Status}";
        }

        var lines = new List<string>
        {
            $"Status: {result.Status}",
            $"Summary: {result.Summary}",
            $"Tool calls: {subAgentRun.ToolCallCount}/{subAgentRun.MaxToolCalls}"
        };
        if (result.Findings.Count > 0)
        {
            lines.Add("Findings:");
            lines.AddRange(result.Findings.Select(finding => "- " + finding));
        }

        if (result.ArtifactRefs.Count > 0)
        {
            lines.Add("Artifact refs:");
            lines.AddRange(result.ArtifactRefs.Select(artifact => "- " + artifact));
        }

        if (!string.IsNullOrWhiteSpace(result.RecommendedNextStep))
        {
            lines.Add("Next: " + result.RecommendedNextStep);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static AgentSubAgentRun ToAgentSubAgentRun(SubAgentRun subAgentRun)
    {
        var result = subAgentRun.Result;
        return new AgentSubAgentRun
        {
            Id = subAgentRun.Id,
            ParentRunId = subAgentRun.ParentRunId,
            TemplateId = subAgentRun.TemplateId,
            Task = subAgentRun.Task,
            Status = subAgentRun.Status.ToString(),
            Summary = SensitiveDataRedactor.RedactText(result?.Summary ?? ""),
            RecommendedNextStep = SensitiveDataRedactor.RedactText(result?.RecommendedNextStep ?? ""),
            MaxToolCalls = subAgentRun.MaxToolCalls,
            ToolCallCount = subAgentRun.ToolCallCount,
            StartedAt = subAgentRun.StartedAt,
            CompletedAt = subAgentRun.CompletedAt,
            Findings = result?.Findings.Select(SensitiveDataRedactor.RedactText).ToList() ?? [],
            ArtifactRefs = result?.ArtifactRefs.Select(SensitiveDataRedactor.RedactText).ToList() ?? [],
            ToolCalls = subAgentRun.ToolCalls.Select(call => new AgentSubAgentToolCall
            {
                ToolCallId = call.ToolCallId,
                ToolName = call.ToolName,
                ArgumentsJson = SensitiveDataRedactor.RedactText(call.ArgumentsJson),
                IsError = call.IsError,
                ResultSummary = SensitiveDataRedactor.RedactText(call.ResultSummary)
            }).ToList()
        };
    }

    private static ChatRequest AppendSubAgentResultMessage(ChatRequest request, SubAgentRun subAgentRun)
    {
        var messages = request.Messages.ToList();
        messages.Add(new ChatMessage
        {
            Role = ChatRole.System,
            Content = $"{FormatTemplateName(subAgentRun.TemplateId)} sub-agent result:\n" + FormatSubAgentResult(subAgentRun),
            CreatedAt = DateTimeOffset.Now
        });
        return new ChatRequest
        {
            Model = request.Model,
            Temperature = request.Temperature,
            Messages = messages,
            Tools = request.Tools
        };
    }

    private static void RecordSubAgentArtifact(AgentRun run, AgentStep step, SubAgentRun subAgentRun)
    {
        if (subAgentRun.Result is null)
        {
            return;
        }

        run.Artifacts.Add(new AgentArtifact
        {
            RunId = run.Id,
            StepId = step.Id,
            ToolName = subAgentRun.TemplateId,
            Kind = "sub_agent_result",
            Summary = SensitiveDataRedactor.RedactText(subAgentRun.Result.Summary),
            Content = SensitiveDataRedactor.RedactText(FormatSubAgentResult(subAgentRun)),
            CreatedAt = DateTimeOffset.Now,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subAgentRunId"] = subAgentRun.Id,
                ["templateId"] = subAgentRun.TemplateId,
                ["status"] = subAgentRun.Status.ToString(),
                ["toolCallCount"] = subAgentRun.ToolCallCount.ToString()
            }
        });
    }

    private static string FormatTemplateName(string templateId)
    {
        return string.IsNullOrWhiteSpace(templateId)
            ? "Sub-agent"
            : char.ToUpperInvariant(templateId[0]) + templateId[1..];
    }

    // Groups the scheduled sub-agents into execution layers so
    // independent ones run in parallel (Task.WhenAll). The full
    // dependency-DAG algorithm (cycle detection, topologically-
    // ordered layers, safety counter against a misbehaving
    // coordinator) used to live here as a 50-line method.
    // Today the only template the coordinator schedules is the
    // read-only "explorer", and explorer plans don't carry
    // dependencies on each other — so the algorithm collapsed
    // to a single layer in the common case. Returning a one-
    // element wrapper here keeps the caller (which awaits one
    // Task.WhenAll per layer) and the AgentSubAgentScheduleDecision
    // .DependsOn shape intact; when worker / verifier templates
    // land with real intra-batch dependencies, the topological
    // algorithm comes back as a non-public helper.
    public static IReadOnlyList<IReadOnlyList<AgentSubAgentScheduleDecision>> ComputeSubAgentExecutionLayers(
        IReadOnlyList<AgentSubAgentScheduleDecision> scheduled)
    {
        return scheduled.Count == 0 ? [] : [scheduled];
    }
}
