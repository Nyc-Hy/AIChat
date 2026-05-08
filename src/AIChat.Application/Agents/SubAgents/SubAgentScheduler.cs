using AIChat.Application.Agents.Templates;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentScheduler
{
    private readonly AgentRunner _runner;
    private readonly AgentTemplateCatalog _templateCatalog;
    private readonly AgentPromptComposer _promptComposer;
    private readonly HashSet<string> _activeTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SubAgentScheduler(
        AgentRunner runner,
        AgentTemplateCatalog? templateCatalog = null,
        AgentPromptComposer? promptComposer = null)
    {
        _runner = runner;
        _templateCatalog = templateCatalog ?? new AgentTemplateCatalog();
        _promptComposer = promptComposer ?? new AgentPromptComposer();
    }

    public async Task<SubAgentRun> RunAsync(
        SubAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = _templateCatalog.Get(request.TemplateId);
        var run = new SubAgentRun
        {
            ParentRunId = request.ParentRunId,
            TemplateId = template.Id,
            Task = request.Task,
            ContextPack = request.ContextPack,
            MaxToolCalls = Math.Max(1, request.MaxToolCalls)
        };

        var taskKey = $"{request.ParentRunId}:{NormalizeTask(request.Task)}";
        lock (_gate)
        {
            if (_activeTasks.Contains(taskKey))
            {
                return Complete(run, SubAgentStatus.Rejected, "Duplicate unresolved sub-agent task.", [], "Wait for the existing sub-agent to finish.");
            }

            _activeTasks.Add(taskKey);
        }

        try
        {
            if (!template.CanWrite && request.WriteScope.Count > 0)
            {
                return Complete(run, SubAgentStatus.Rejected, "Read-only template cannot receive a write scope.", [], "Use a worker template for scoped edits.");
            }

            var messages = BuildMessages(template, request);
            var context = new AgentRunContext
            {
                ProjectPath = request.ProjectPath,
                EnabledToolIds = template.DefaultToolIds,
                ToolPermissionModes = template.BuildPermissionModes(),
                MaxToolRounds = run.MaxToolCalls,
                InputArtifacts = request.InputArtifacts
            };
            var findings = new List<string>();
            var content = "";

            await foreach (var agentEvent in _runner.RunAsync(new ChatRequest
                           {
                               Model = request.Settings.Model,
                               Temperature = request.Settings.Temperature,
                               Messages = messages
                           },
                           request.Settings,
                           context,
                           cancellationToken))
            {
                switch (agentEvent.Type)
                {
                    case AgentRunEventType.ToolCall:
                        if (agentEvent.ToolCall is null)
                        {
                            break;
                        }

                        if (!template.CanWrite && IsMutationTool(agentEvent.ToolCall.Name))
                        {
                            return Complete(run, SubAgentStatus.Failed, $"Read-only sub-agent attempted forbidden tool: {agentEvent.ToolCall.Name}.", findings, "Re-run with a worker template if edits are required.");
                        }

                        run.ToolCallCount++;
                        if (run.ToolCallCount > run.MaxToolCalls)
                        {
                            return Complete(run, SubAgentStatus.BudgetExceeded, "Sub-agent tool budget exceeded.", findings, "Continue with a larger sub-agent budget if needed.");
                        }

                        run.ToolCalls.Add(new SubAgentToolCallRecord
                        {
                            ParentRunId = request.ParentRunId,
                            SubAgentRunId = run.Id,
                            ToolCallId = agentEvent.ToolCall.Id,
                            ToolName = agentEvent.ToolCall.Name,
                            ArgumentsJson = agentEvent.ToolCall.ArgumentsJson
                        });
                        break;
                    case AgentRunEventType.ToolResult:
                        var record = agentEvent.ToolCall is null
                            ? null
                            : run.ToolCalls.LastOrDefault(call => call.ToolCallId == agentEvent.ToolCall.Id);
                        if (record is not null && agentEvent.ToolResult is not null)
                        {
                            record.IsError = agentEvent.ToolResult.IsError;
                            record.ResultSummary = SummarizeToolResult(agentEvent.ToolResult);
                            if (agentEvent.ToolResult.IsError)
                            {
                                findings.Add($"{agentEvent.ToolResult.ToolName} failed: {record.ResultSummary}");
                            }
                        }
                        break;
                    case AgentRunEventType.ContentDelta:
                        content += agentEvent.Content;
                        break;
                    case AgentRunEventType.Error:
                        findings.Add(agentEvent.Content);
                        return Complete(run, SubAgentStatus.Failed, agentEvent.Content, findings, "Inspect the sub-agent error and retry with narrower context.");
                    case AgentRunEventType.BudgetExceeded:
                        return Complete(run, SubAgentStatus.BudgetExceeded, agentEvent.Content, findings, "Continue with a larger sub-agent budget if needed.");
                    case AgentRunEventType.Completed:
                        return Complete(run, SubAgentStatus.Completed, content, findings, "Use the findings in the parent run.");
                }
            }

            return Complete(run, SubAgentStatus.Completed, content, findings, "Use the findings in the parent run.");
        }
        catch (OperationCanceledException)
        {
            return Complete(run, SubAgentStatus.Cancelled, "Sub-agent was cancelled.", [], "Resume or rerun the sub-agent when ready.");
        }
        finally
        {
            lock (_gate)
            {
                _activeTasks.Remove(taskKey);
            }
        }
    }

    private IReadOnlyList<ChatMessage> BuildMessages(AgentTemplate template, SubAgentRunRequest request)
    {
        var contextRefs = request.ContextPack?.ToPromptRefs() ?? [];
        return _promptComposer.Compose(new AgentPromptComposeRequest
        {
            Profile = template.PromptProfile,
            Goal = request.Task,
            AllowedTools = template.DefaultToolIds,
            ContextRefs = contextRefs,
            ResponseRequirements = template.OutputSchema
        }).Messages;
    }

    private static SubAgentRun Complete(
        SubAgentRun run,
        SubAgentStatus status,
        string summary,
        IReadOnlyList<string> findings,
        string recommendedNextStep)
    {
        run.Status = status;
        run.CompletedAt = DateTimeOffset.Now;
        run.Result = new SubAgentResult
        {
            Status = status,
            Summary = string.IsNullOrWhiteSpace(summary) ? status.ToString() : summary.Trim(),
            Findings = findings.ToList(),
            ChangedFiles = [],
            ArtifactRefs = run.ContextPack?.ArtifactRefs ?? [],
            RecommendedNextStep = recommendedNextStep
        };
        return run;
    }

    private static string SummarizeToolResult(AgentToolResult result)
    {
        var content = result.ContentForModel.ReplaceLineEndings(" ").Trim();
        return content.Length <= 240 ? content : content[..240] + "...";
    }

    private static bool IsMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit" or "run_shell";
    }

    private static string NormalizeTask(string task)
    {
        return string.Join(' ', task.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
