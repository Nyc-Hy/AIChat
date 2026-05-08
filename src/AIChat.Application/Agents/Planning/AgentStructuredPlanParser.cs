using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Planning;

public sealed class AgentStructuredPlanParser
{
    public const int MaxTaskCount = 12;
    public const int MaxPlannedSubAgentCount = 4;

    private static readonly HashSet<string> KnownSubAgentTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "verifier",
        "reviewer",
        "summarizer"
    };

    private static readonly HashSet<string> KnownPhases = new(StringComparer.OrdinalIgnoreCase)
    {
        "planning",
        "gathering_context",
        "executing",
        "verifying",
        "summarizing"
    };

    public AgentStructuredPlan ParseOrFallback(string json, AgentPlanningRequest request)
    {
        try
        {
            var plan = Parse(json);
            if (plan.Phases.Count == 0 || plan.Phases.Sum(phase => phase.Tasks.Count) == 0)
            {
                return CreateFallback(request);
            }

            Normalize(plan, request);
            return plan;
        }
        catch (JsonException)
        {
            return CreateFallback(request);
        }
    }

    public static AgentStructuredPlan CreateFallback(AgentPlanningRequest request)
    {
        return new AgentStructuredPlan
        {
            Summary = string.IsNullOrWhiteSpace(request.Goal) ? "执行用户请求" : request.Goal,
            IsFallback = true,
            SuggestedTools = request.EnabledToolIds.Take(4).ToList(),
            Budget = new AgentPlanBudget
            {
                MaxToolCalls = 6,
                TokenBudget = 12000,
                Notes = "planner output was unavailable or invalid"
            },
            Phases =
            [
                new AgentPlanPhase
                {
                    Name = "executing",
                    Objective = "按用户目标完成当前任务",
                    Tasks =
                    [
                        new AgentPlanTask
                        {
                            Phase = "executing",
                            Title = string.IsNullOrWhiteSpace(request.Goal) ? "完成当前请求" : request.Goal,
                            Details = "使用现有 Agent 工具链逐步读取上下文、执行必要修改并验证结果。",
                            Risk = AgentPlanRisk.Medium,
                            SuggestedTools = request.EnabledToolIds.Take(4).ToList(),
                            Budget = new AgentPlanBudget { MaxToolCalls = 6, TokenBudget = 12000 },
                            Order = 0
                        }
                    ]
                }
            ]
        };
    }

    private static AgentStructuredPlan Parse(string json)
    {
        using var document = JsonDocument.Parse(ExtractJson(json));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Plan root must be an object.");
        }

        var plan = new AgentStructuredPlan
        {
            Summary = GetString(root, "summary"),
            SuggestedTools = GetStringArray(root, "suggestedTools"),
            SuggestedContext = GetStringArray(root, "suggestedContext"),
            Budget = GetBudget(root, "budget"),
            SubAgents = GetSubAgents(root)
        };

        if (root.TryGetProperty("phases", out var phasesElement) &&
            phasesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var phaseElement in phasesElement.EnumerateArray())
            {
                if (phaseElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var phaseName = FirstNonBlank(
                    GetString(phaseElement, "name"),
                    GetString(phaseElement, "phase"),
                    "executing");
                var phase = new AgentPlanPhase
                {
                    Name = phaseName,
                    Objective = GetString(phaseElement, "objective")
                };

                if (phaseElement.TryGetProperty("tasks", out var tasksElement) &&
                    tasksElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var taskElement in tasksElement.EnumerateArray())
                    {
                        if (taskElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        phase.Tasks.Add(new AgentPlanTask
                        {
                            Phase = phaseName,
                            Title = FirstNonBlank(GetString(taskElement, "title"), GetString(taskElement, "name")),
                            Details = FirstNonBlank(GetString(taskElement, "details"), GetString(taskElement, "description")),
                            Risk = ParseRisk(GetString(taskElement, "risk")),
                            SuggestedTools = GetStringArray(taskElement, "suggestedTools"),
                            SuggestedContext = GetStringArray(taskElement, "suggestedContext"),
                            Budget = GetBudget(taskElement, "budget")
                        });
                    }
                }

                plan.Phases.Add(phase);
            }
        }

        return plan;
    }

    private static void Normalize(AgentStructuredPlan plan, AgentPlanningRequest request)
    {
        plan.Summary = FirstNonBlank(plan.Summary, request.Goal, "执行用户请求");
        plan.SuggestedTools = NormalizeTools(plan.SuggestedTools, request.EnabledToolIds);
        plan.SuggestedContext = NormalizeStrings(plan.SuggestedContext);
        plan.Budget = NormalizeBudget(plan.Budget, 8, 12000);
        plan.SubAgents = NormalizeSubAgents(plan.SubAgents);

        var order = 0;
        var remaining = MaxTaskCount;
        foreach (var phase in plan.Phases.ToList())
        {
            phase.Name = NormalizePhase(phase.Name);
            phase.Objective = FirstNonBlank(phase.Objective, phase.Name);
            var tasks = phase.Tasks
                .Where(task => !string.IsNullOrWhiteSpace(task.Title))
                .Take(remaining)
                .ToList();
            phase.Tasks.Clear();
            foreach (var task in tasks)
            {
                task.Phase = phase.Name;
                task.Title = task.Title.Trim();
                task.Details = task.Details.Trim();
                task.SuggestedTools = NormalizeTools(task.SuggestedTools, request.EnabledToolIds);
                task.SuggestedContext = NormalizeStrings(task.SuggestedContext);
                task.Budget = NormalizeBudget(task.Budget, 2, 3000);
                task.Order = order++;
                phase.Tasks.Add(task);
            }

            remaining = Math.Max(0, MaxTaskCount - order);
        }

        plan.Phases = plan.Phases.Where(phase => phase.Tasks.Count > 0).ToList();
        if (plan.Phases.Count == 0)
        {
            var fallback = CreateFallback(request);
            plan.IsFallback = true;
            plan.Summary = fallback.Summary;
            plan.SuggestedTools = fallback.SuggestedTools;
            plan.SuggestedContext = fallback.SuggestedContext;
            plan.Budget = fallback.Budget;
            plan.SubAgents = fallback.SubAgents;
            plan.Phases = fallback.Phases;
        }
    }

    private static List<AgentPlannedSubAgent> NormalizeSubAgents(IReadOnlyList<AgentPlannedSubAgent> subAgents)
    {
        var order = 0;
        return subAgents
            .Where(agent => !string.IsNullOrWhiteSpace(agent.Task))
            .Take(MaxPlannedSubAgentCount)
            .Select(agent => new AgentPlannedSubAgent
            {
                Id = string.IsNullOrWhiteSpace(agent.Id) ? Guid.NewGuid().ToString("N") : agent.Id,
                TemplateId = NormalizeTemplate(agent.TemplateId),
                Phase = NormalizePhase(FirstNonBlank(agent.Phase, "gathering_context")),
                Task = agent.Task.Trim(),
                Reason = agent.Reason.Trim(),
                MaxToolCalls = Math.Clamp(agent.MaxToolCalls <= 0 ? 4 : agent.MaxToolCalls, 1, 8),
                Order = order++,
                DependsOn = NormalizeStrings(agent.DependsOn),
                WriteScope = NormalizeStrings(agent.WriteScope)
            })
            .ToList();
    }

    private static string NormalizeTemplate(string templateId)
    {
        var normalized = templateId.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
        return KnownSubAgentTemplates.Contains(normalized) ? normalized : "explorer";
    }

    private static string ExtractJson(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed[firstBrace..(lastBrace + 1)];
            }
        }

        return trimmed;
    }

    private static string NormalizePhase(string phase)
    {
        var normalized = phase.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
        return KnownPhases.Contains(normalized) ? normalized : "executing";
    }

    private static AgentPlanRisk ParseRisk(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "low" or "低" => AgentPlanRisk.Low,
            "high" or "高" => AgentPlanRisk.High,
            _ => AgentPlanRisk.Medium
        };
    }

    private static AgentPlanBudget NormalizeBudget(AgentPlanBudget budget, int defaultToolCalls, int defaultTokens)
    {
        return new AgentPlanBudget
        {
            MaxToolCalls = Math.Clamp(budget.MaxToolCalls <= 0 ? defaultToolCalls : budget.MaxToolCalls, 1, 20),
            TokenBudget = Math.Clamp(budget.TokenBudget <= 0 ? defaultTokens : budget.TokenBudget, 1000, 200000),
            Notes = budget.Notes.Trim()
        };
    }

    private static List<string> NormalizeTools(IReadOnlyList<string> tools, IReadOnlyList<string> enabledTools)
    {
        var enabled = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase);
        return NormalizeStrings(tools)
            .Where(tool => enabled.Count == 0 || enabled.Contains(tool))
            .ToList();
    }

    private static List<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AgentPlanBudget GetBudget(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return new AgentPlanBudget();
        }

        return new AgentPlanBudget
        {
            MaxToolCalls = GetInt(value, "maxToolCalls"),
            TokenBudget = GetInt(value, "tokenBudget"),
            Notes = GetString(value, "notes")
        };
    }

    private static List<AgentPlannedSubAgent> GetSubAgents(JsonElement root)
    {
        if (!root.TryGetProperty("subAgents", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AgentPlannedSubAgent>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new AgentPlannedSubAgent
            {
                TemplateId = FirstNonBlank(GetString(item, "templateId"), GetString(item, "template"), "explorer"),
                Phase = FirstNonBlank(GetString(item, "phase"), "gathering_context"),
                Task = FirstNonBlank(GetString(item, "task"), GetString(item, "title")),
                Reason = GetString(item, "reason"),
                MaxToolCalls = GetInt(item, "maxToolCalls"),
                DependsOn = GetStringArray(item, "dependsOn"),
                WriteScope = GetStringArray(item, "writeScope")
            });
        }

        return result;
    }

    private static List<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
