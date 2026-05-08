using AIChat.Domain.Chat;
using AIChat.Application.Context;
using AIChat.Application.Agents.Templates;

namespace AIChat.Application.Agents.Coordinator;

public sealed class AgentCoordinator
{
    private readonly AgentTemplateCatalog _templateCatalog;

    public AgentCoordinator(AgentTemplateCatalog? templateCatalog = null)
    {
        _templateCatalog = templateCatalog ?? new AgentTemplateCatalog();
    }

    public AgentTemplate SelectTemplate(AgentRunPhase phase, bool requiresWrite = false)
    {
        return _templateCatalog.SelectForPhase(phase, requiresWrite);
    }

    public bool ShouldRunExplorer(
        AgentStructuredPlan? plan,
        TaskContextPack? contextPack,
        string goal,
        bool requiresWrite)
    {
        if (plan is null || contextPack is null)
        {
            return false;
        }

        if (plan.IsFallback && contextPack.IncludedFiles.Count == 0 && contextPack.ArtifactRefs.Count == 0)
        {
            return false;
        }

        if (plan.Phases.Any(phase =>
                IsGatheringPhase(phase.Name) ||
                ContainsExplorerHint(phase.Objective) ||
                phase.Tasks.Any(task =>
                    ContainsExplorerHint(task.Title) ||
                    ContainsExplorerHint(task.Details) ||
                    task.SuggestedTools.Any(IsExplorerTool))))
        {
            return true;
        }

        return requiresWrite && ContainsExplorerHint(goal);
    }

    public IReadOnlyList<AgentPlannedSubAgent> SelectPlannedSubAgents(
        AgentStructuredPlan? plan,
        TaskContextPack? contextPack,
        string goal,
        bool requiresWrite)
    {
        return CreateSubAgentSchedule("", plan, contextPack, goal, requiresWrite)
            .Where(decision => string.Equals(decision.Status, "Scheduled", StringComparison.OrdinalIgnoreCase))
            .Select(decision => new AgentPlannedSubAgent
            {
                Id = decision.PlannedSubAgentId,
                TemplateId = decision.TemplateId,
                Phase = decision.Phase,
                Task = decision.Task,
                Reason = decision.Reason,
                MaxToolCalls = decision.MaxToolCalls,
                Order = decision.Order,
                DependsOn = decision.DependsOn.ToList(),
                WriteScope = decision.WriteScope.ToList()
            })
            .ToList();
    }

    public IReadOnlyList<AgentSubAgentScheduleDecision> CreateSubAgentSchedule(
        string runId,
        AgentStructuredPlan? plan,
        TaskContextPack? contextPack,
        string goal,
        bool requiresWrite)
    {
        if (plan is not null)
        {
            var decisions = BuildPlannedSubAgentDecisions(runId, plan.SubAgents)
                .ToList();
            if (decisions.Count > 0)
            {
                return decisions;
            }
        }

        return ShouldRunExplorer(plan, contextPack, goal, requiresWrite)
            ?
            [
                new AgentSubAgentScheduleDecision
                {
                    RunId = runId,
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "",
                    Reason = "Coordinator fallback for context gathering.",
                    Status = "Scheduled",
                    MaxToolCalls = 4
                }
            ]
            : [];
    }

    public AgentPhaseTransition StartPhase(AgentRun run, AgentRunPhase phase, string summary = "")
    {
        var key = ToPhaseKey(phase);
        if (string.Equals(run.Phase, key, StringComparison.OrdinalIgnoreCase) &&
            run.PhaseHistory.LastOrDefault()?.Status == "running")
        {
            run.CurrentPhaseSummary = summary;
            var active = run.PhaseHistory.Last();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                active.Summary = summary;
            }

            return new AgentPhaseTransition(phase, key, "running", summary);
        }

        CompleteActivePhase(run, "completed", run.CurrentPhaseSummary);
        run.Phase = key;
        run.CurrentPhaseSummary = summary;
        run.PhaseHistory.Add(new AgentRunPhaseRecord
        {
            RunId = run.Id,
            Phase = key,
            Status = "running",
            Summary = summary,
            StartedAt = DateTimeOffset.Now
        });
        return new AgentPhaseTransition(phase, key, "running", summary);
    }

    public AgentPhaseTransition CompletePhase(AgentRun run, AgentRunPhase phase, string summary = "")
    {
        var key = ToPhaseKey(phase);
        if (!string.Equals(run.Phase, key, StringComparison.OrdinalIgnoreCase))
        {
            StartPhase(run, phase, summary);
        }

        CompleteActivePhase(run, "completed", summary);
        run.Phase = key;
        run.CurrentPhaseSummary = summary;
        return new AgentPhaseTransition(phase, key, "completed", summary);
    }

    public AgentPhaseTransition CompleteRun(AgentRun run, AgentRunStatus status, string summary = "")
    {
        var phase = status switch
        {
            AgentRunStatus.Cancelled => AgentRunPhase.Cancelled,
            AgentRunStatus.Failed => AgentRunPhase.Failed,
            _ => AgentRunPhase.Completed
        };
        var terminalStatus = status switch
        {
            AgentRunStatus.Cancelled => "cancelled",
            AgentRunStatus.Failed => "failed",
            _ => "completed"
        };

        CompleteActivePhase(run, terminalStatus, summary);
        run.Complete(status, completionReason: summary);
        run.CurrentPhaseSummary = summary;
        return new AgentPhaseTransition(phase, ToPhaseKey(phase), terminalStatus, summary);
    }

    public static AgentRunPhase ClassifyToolPhase(string toolName)
    {
        return toolName switch
        {
            "list_files" or "read_file" or "read_input_artifact" or "search_text" or "git_status" or "git_diff" => AgentRunPhase.GatheringContext,
            "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit" => AgentRunPhase.Executing,
            "run_build" or "run_test" => AgentRunPhase.Verifying,
            "update_plan" => AgentRunPhase.Planning,
            _ => AgentRunPhase.Executing
        };
    }

    private static bool IsGatheringPhase(string phaseName)
    {
        return phaseName.Contains("gather", StringComparison.OrdinalIgnoreCase) ||
               phaseName.Contains("context", StringComparison.OrdinalIgnoreCase) ||
               phaseName.Contains("explor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExplorerHint(string value)
    {
        return value.Contains("inspect", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("explore", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("read", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("search", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("context", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("查看", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("读取", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("搜索", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("上下文", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplorerTool(string toolName)
    {
        return toolName is "list_files" or "read_file" or "read_input_artifact" or "search_text" or "git_status" or "git_diff";
    }

    private static IEnumerable<AgentSubAgentScheduleDecision> BuildPlannedSubAgentDecisions(
        string runId,
        IReadOnlyList<AgentPlannedSubAgent> plannedSubAgents)
    {
        var scheduledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduledTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in plannedSubAgents.OrderBy(agent => agent.Order))
        {
            var decision = ToScheduleDecision(runId, agent);
            var taskKey = NormalizeTaskKey(agent.TemplateId, agent.Task);
            if (!string.Equals(agent.TemplateId, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                decision.Status = "Skipped";
                decision.SkipReason = $"Unsupported sub-agent template before execution: {agent.TemplateId}.";
            }
            else if (agent.WriteScope.Count > 0)
            {
                decision.Status = "Skipped";
                decision.SkipReason = "Read-only explorer sub-agent cannot receive a write scope.";
            }
            else if (!IsBeforeExecutionPhase(agent.Phase))
            {
                decision.Status = "Skipped";
                decision.SkipReason = $"Sub-agent phase is not runnable before execution: {agent.Phase}.";
            }
            else if (agent.DependsOn.Any(dependency => !scheduledIds.Contains(dependency)))
            {
                decision.Status = "Skipped";
                decision.SkipReason = "Sub-agent dependencies are not satisfied.";
            }
            else if (!scheduledTasks.Add(taskKey))
            {
                decision.Status = "Skipped";
                decision.SkipReason = "Duplicate sub-agent task.";
            }
            else
            {
                scheduledIds.Add(agent.Id);
            }

            yield return decision;
        }
    }

    private static AgentSubAgentScheduleDecision ToScheduleDecision(string runId, AgentPlannedSubAgent agent)
    {
        return new AgentSubAgentScheduleDecision
        {
            RunId = runId,
            PlannedSubAgentId = agent.Id,
            TemplateId = agent.TemplateId,
            Phase = agent.Phase,
            Task = agent.Task,
            Reason = agent.Reason,
            Status = "Scheduled",
            MaxToolCalls = agent.MaxToolCalls,
            Order = agent.Order,
            DependsOn = agent.DependsOn.ToList(),
            WriteScope = agent.WriteScope.ToList()
        };
    }

    private static bool IsBeforeExecutionPhase(string phase)
    {
        return string.Equals(phase, "gathering_context", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(phase, "planning", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTaskKey(string templateId, string task)
    {
        return $"{templateId}:{string.Join(' ', task.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))}";
    }

    public static string ToPhaseKey(AgentRunPhase phase)
    {
        return phase switch
        {
            AgentRunPhase.Planning => "planning",
            AgentRunPhase.GatheringContext => "gathering_context",
            AgentRunPhase.Executing => "executing",
            AgentRunPhase.Verifying => "verifying",
            AgentRunPhase.Repairing => "repairing",
            AgentRunPhase.Summarizing => "summarizing",
            AgentRunPhase.WaitingForUser => "waiting_for_user",
            AgentRunPhase.Failed => "failed",
            AgentRunPhase.Cancelled => "cancelled",
            _ => "completed"
        };
    }

    private static void CompleteActivePhase(AgentRun run, string status, string summary)
    {
        var active = run.PhaseHistory.LastOrDefault(record => record.Status == "running");
        if (active is null)
        {
            return;
        }

        active.Status = status;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            active.Summary = summary;
        }

        active.CompletedAt = DateTimeOffset.Now;
    }
}
