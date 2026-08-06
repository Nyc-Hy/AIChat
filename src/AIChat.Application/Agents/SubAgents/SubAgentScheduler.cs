using AIChat.Application.Agents.Templates;
using AIChat.Application.Prompting;
using AIChat.Application.Security;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.SubAgents;

public sealed class SubAgentScheduler
{
    private readonly AgentRunner _runner;
    private readonly AgentTemplateCatalog _templateCatalog;
    private readonly AgentPromptComposer _promptComposer;
    private readonly HashSet<string> _activeTasks = new(StringComparer.OrdinalIgnoreCase);
    // 2026-08-03: per-run CancellationTokenSource so the host
    // can cancel an in-flight sub-agent from the UI without
    // killing the parent AgentHarness run. The CTS is created
    // on RunAsync start and removed (cancelled first) on
    // completion / failure / cancellation. Linked into the
    // caller's token so a parent cancellation cascades down.
    private readonly Dictionary<string, CancellationTokenSource> _activeRuns = new(StringComparer.OrdinalIgnoreCase);
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
            Id = request.RunId,
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

            // 2026-08-03: register a per-run CTS so the host can
            // cancel an individual sub-agent from the UI. The
            // caller's token is linked so a parent cancel still
            // cascades; the registered CTS is the token the host
            // can signal independently.
            var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_gate)
            {
                _activeRuns[run.Id] = runCts;
            }

            try
            {
                await foreach (var agentEvent in _runner.RunAsync(new ChatRequest
                               {
                                   Model = request.Settings.Model,
                                   Temperature = request.Settings.Temperature,
                                   Messages = messages
                               },
                               request.Settings,
                               context,
                               runCts.Token))
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
                                ArgumentsJson = SensitiveDataRedactor.RedactText(agentEvent.ToolCall.ArgumentsJson)
                            });
                            break;
                        case AgentRunEventType.ToolResult:
                            var record = agentEvent.ToolCall is null
                                ? null
                                : run.ToolCalls.LastOrDefault(call => call.ToolCallId == agentEvent.ToolCall.Id);
                            if (record is not null && agentEvent.ToolResult is not null)
                            {
                                record.IsError = agentEvent.ToolResult.IsError;
                                record.ResultSummary = SensitiveDataRedactor.RedactText(SummarizeToolResult(agentEvent.ToolResult));
                                if (agentEvent.ToolResult.IsError)
                                {
                                    findings.Add(SensitiveDataRedactor.RedactText($"{agentEvent.ToolResult.ToolName} failed: {record.ResultSummary}"));
                                }
                            }
                            break;
                        case AgentRunEventType.ContentDelta:
                            content += SensitiveDataRedactor.RedactText(agentEvent.Content);
                            break;
                        case AgentRunEventType.RunUsage:
                            // 2026-08-05: sub-agents can also
                            // carry per-call usage. We don't
                            // surface it anywhere today
                            // (sub-agent results are summarized
                            // as text), but the type must
                            // match the switch — unhandled
                            // cases were causing a compile
                            // error when the new RunUsage
                            // enum value was added.
                            break;
                        case AgentRunEventType.Error:
                            findings.Add(SensitiveDataRedactor.RedactText(agentEvent.Content));
                            return Complete(run, SubAgentStatus.Failed, agentEvent.Content, findings, "Inspect the sub-agent error and retry with narrower context.");
                        case AgentRunEventType.Cancelled:
                            return Complete(run, SubAgentStatus.Cancelled, "Sub-agent was cancelled by the user.", findings, "Resume or rerun the sub-agent when ready.");
                        case AgentRunEventType.BudgetExceeded:
                            return Complete(run, SubAgentStatus.BudgetExceeded, agentEvent.Content, findings, "Continue with a larger sub-agent budget if needed.");
                        case AgentRunEventType.Completed:
                            return Complete(run, SubAgentStatus.Completed, content, findings, "Use the findings in the parent run.");
                    }
                }
            }
            catch (OperationCanceledException) when (runCts.IsCancellationRequested)
            {
                return Complete(run, SubAgentStatus.Cancelled, "Sub-agent was cancelled by the user.", findings, "Resume or rerun the sub-agent when ready.");
            }
            catch (OperationCanceledException)
            {
                // Parent cancelled — pass through as cancelled
                // so the host's UI shows the same final state
                // regardless of who triggered the stop.
                return Complete(run, SubAgentStatus.Cancelled, "Sub-agent was cancelled.", findings, "Resume or rerun the sub-agent when ready.");
            }

            return Complete(run, SubAgentStatus.Completed, content, findings, "Use the findings in the parent run.");
        }
        finally
        {
            lock (_gate)
            {
                _activeTasks.Remove(taskKey);
                if (_activeRuns.TryGetValue(run.Id, out var cts))
                {
                    _activeRuns.Remove(run.Id);
                    cts.Dispose();
                }
            }
        }
    }

    // 2026-08-03: cancel an in-flight sub-agent by id. Returns
    // true when the run was found and signalled. The
    // OperationCanceledException is converted to
    // SubAgentStatus.Cancelled by the RunAsync catch block, so
    // the host's SubAgentRuns row updates from 'Running' to
    // 'Cancelled' within one event-loop turn. Cancelling an
    // already-finished run is a no-op and returns false; the
    // UI's stop button is hidden for non-running rows so the
    // caller does not have to interpret the return value.
    public bool CancelAsync(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return false;
        }
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_activeRuns.TryGetValue(runId, out cts))
            {
                return false;
            }
        }
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Race: the run finished and disposed the CTS
            // between the lock release and the Cancel call.
            return false;
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
            Summary = SensitiveDataRedactor.RedactText(string.IsNullOrWhiteSpace(summary) ? status.ToString() : summary.Trim()),
            Findings = findings.Select(SensitiveDataRedactor.RedactText).ToList(),
            ChangedFiles = [],
            ArtifactRefs = run.ContextPack?.ArtifactRefs.Select(SensitiveDataRedactor.RedactText).ToList() ?? [],
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
