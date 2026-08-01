using System.Runtime.CompilerServices;
using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Agents.Planning;
using AIChat.Application.Agents.SubAgents;
using AIChat.Application.Prompting;
using AIChat.Application.Security;
using AIChat.Application.Tools;
using AIChat.Application.Verification;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Thin harness around the model/tool loop. It owns run/step recording while the
// UI remains responsible for rendering events and collecting user approvals.
public sealed class AgentHarness
{
    private readonly AgentRunner _agentRunner;
    private readonly AgentPlanner? _planner;
    private readonly AgentCoordinator _coordinator;
    private readonly AgentPromptComposer _promptComposer;
    private readonly SubAgentScheduler? _subAgentScheduler;
    private readonly AgentTaskClassifier _taskClassifier;
    private readonly AgentTaskExecutionPolicyBuilder _executionPolicyBuilder;

    public AgentHarness(
        AgentRunner agentRunner,
        AgentPlanner? planner = null,
        AgentCoordinator? coordinator = null,
        AgentPromptComposer? promptComposer = null,
        SubAgentScheduler? subAgentScheduler = null,
        AgentTaskClassifier? taskClassifier = null,
        AgentTaskExecutionPolicyBuilder? executionPolicyBuilder = null)
    {
        _agentRunner = agentRunner;
        _planner = planner;
        _coordinator = coordinator ?? new AgentCoordinator();
        _promptComposer = promptComposer ?? new AgentPromptComposer();
        _subAgentScheduler = subAgentScheduler;
        _taskClassifier = taskClassifier ?? new AgentTaskClassifier();
        _executionPolicyBuilder = executionPolicyBuilder ?? new AgentTaskExecutionPolicyBuilder();
    }

    // Per-run step counter. Starts at 1 (the first emitted step
    // is number 1) and is bumped after each step is added. The
    // counter is a private instance field rather than a local in
    // RunAsync because the eventual plan is to extract each
    // phase (planner / context / sub-agent / tool-loop) into its
    // own IAsyncEnumerable helper — IAsyncEnumerable helpers
    // can't share `ref` locals with their caller, so a per-
    // instance slot is the cleanest shared state. AgentHarness
    // is constructed per-run (AgentRunnerViewModel.cs new's one
    // harness per user message), so this field is never
    // concurrent across runs.
    private int _nextStepNumber = 1;

    // Per-run execution request. Starts as the user-supplied
    // ChatRequest, then gets re-Appended to with a synthetic
    // "sub-agent result" message after each completed sub-agent
    // (so the main model call sees the sub-agent findings as
    // part of its conversation). Same justification as
    // _nextStepNumber: IAsyncEnumerable phase helpers can't
    // share `ref` locals across yield boundaries, and this
    // request is the canonical "input" the tool loop reads at
    // the end of the sub-agent phase.
    private ChatRequest _executionRequest = null!;

    public async IAsyncEnumerable<AgentHarnessEvent> RunAsync(
        AgentHarnessRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var run = CreateRunRecord(request);
        var executionPolicy = ResolveExecutionPolicy(request, run);
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.RunStarted,
            Run = run
        };

        var planner = _planner;
        var shouldPlan = planner is not null && executionPolicy.UsePlanner;
        if (shouldPlan)
        {
            await foreach (var evt in RunPlanPhaseAsync(request, run, cancellationToken))
            {
                yield return evt;
            }
        }

        await foreach (var evt in RunContextPhaseAsync(request, run, executionPolicy))
        {
            yield return evt;
        }

        _executionRequest = request.ChatRequest;
        await foreach (var evt in RunSubAgentPhaseAsync(request, run, executionPolicy, cancellationToken))
        {
            yield return evt;
        }

        await foreach (var evt in RunToolLoopPhaseAsync(request, run, executionPolicy, cancellationToken))
        {
            yield return evt;
        }
    }

    // Shared terminal-event sequence used by both the Cancelled
    // and BudgetExceeded switch arms, and by the BudgetExceeded
    // branch inside the Completed arm. Emits the three events every
    // terminal state needs: ContentDelta (with the user-visible
    // message), PhaseChanged (the final phase), RunCompleted (the
    // final step). The two paths differed only in (message, step
    // title, status, and whether run.ToolBudgetExceeded /
    // run.CompletionReason were pre-set), so the helper takes
    // those four values and does the bookkeeping once. Cancelled
    // runs redact the user-supplied reason; budget-exceeded runs
    // set the canonical "tool budget" message themselves.
    private async IAsyncEnumerable<AgentHarnessEvent> EmitTerminalAsync(
        AgentRun run,
        string message,
        string stepTitle,
        AgentRunStatus status,
        bool isBudgetExceeded)
    {
        if (isBudgetExceeded)
        {
            run.ToolBudgetExceeded = true;
            run.CompletionReason = "已达到工具调用轮数上限。";
        }
        else
        {
            run.CompletionReason = SensitiveDataRedactor.RedactText(message);
        }

        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.ContentDelta,
            Run = run,
            Content = message
        };
        yield return CreatePhaseChanged(run, CompleteRun(run, status));
        var step = AddCompletedStep(
            run,
            run.Steps.Count + 1,
            AgentStepType.Final,
            stepTitle,
            "",
            message);
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.RunCompleted,
            Run = run,
            Step = step
        };
        // IAsyncEnumerable must yield — keep the compiler quiet
        // even when this phase is the terminal one.
        await Task.CompletedTask;
    }

    // Stage 0 of RunAsync: hydrate the AgentRun record from the
    // request and register it on the conversation. Everything in
    // the record is request-derived (Goal, ProjectPath, Model,
    // tools, permissions, workspace snapshot, prep status,
    // context-pack stats) plus the always-default flags
    // (RequiresProjectMutation starts false and flips on the
    // first mutation tool) and timestamps. The one side-effect
    // — appending to AgentRuns — happens here so downstream
    // helpers that scan history (ApplyHistoricalAdjustments,
    // UI consumers) already see this run by the time RunStarted
    // is yielded.
    private AgentRun CreateRunRecord(AgentHarnessRunRequest request)
    {
        var run = new AgentRun
        {
            ConversationId = request.Conversation.Id,
            UserMessageId = request.UserMessageId,
            AssistantMessageId = request.AssistantMessageId,
            Goal = request.Goal,
            ProjectPath = request.Context.ProjectPath,
            Model = request.Settings.Model,
            EnabledTools = request.Context.EnabledToolIds.ToList(),
            ToolPermissionModes = request.Context.ToolPermissionModes.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            WorkspaceBranch = request.WorkspaceBranch,
            WorkspaceChangeCountAtStart = request.WorkspaceChangeCountAtStart,
            WorkspaceChangesWereTruncated = request.WorkspaceChangesWereTruncated,
            ProjectPreparationSucceeded = request.Context.ProjectPreparationSucceeded,
            ProjectPreparationSummary = request.Context.ProjectPreparationSummary,
            ProjectAgentsAvailableAtStart = request.Context.ProjectAgentsAvailable,
            ProjectVerificationCommandCountAtStart = request.Context.ProjectVerificationCommandCount,
            MaxToolRounds = request.Context.MaxToolRounds,
            ContextEstimatedTokens = request.ContextPack?.EstimatedTokens ?? 0,
            ContextRefCount = request.ContextPack?.ToPromptRefs().Count ?? 0,
            RequiresProjectMutation = false,
            ContinuedFromRunId = request.ContinuedFromRunId,
            RetriedFromRunId = request.RetriedFromRunId,
            StartedAt = DateTimeOffset.Now
        };
        request.Conversation.AgentRuns.Add(run);
        return run;
    }

    // Stage 0.5 of RunAsync: classify the task, build the
    // execution policy, fold in historical adjustments, and
    // project the resolved values back onto the run record so
    // RunStarted already carries the right MaxToolRounds /
    // TaskComplexity / ExecutionPolicySummary. The history
    // filter (`item.Id != run.Id`) is needed because
    // CreateRunRecord already appended the new run to
    // AgentRuns — without the filter, ApplyHistoricalAdjustments
    // would see the just-created run in its own history and
    // double-count budget pressure on the very first run of a
    // conversation.
    private AgentTaskExecutionPolicy ResolveExecutionPolicy(AgentHarnessRunRequest request, AgentRun run)
    {
        var taskComplexity = _taskClassifier.Classify(request.Goal, request.Context);
        var policy = _executionPolicyBuilder.Build(
            taskComplexity, request.Context, request.ContextPack,
            !string.IsNullOrWhiteSpace(request.ContinuedFromRunId));
        policy = ApplyHistoricalAdjustments(
            policy, request.Context,
            request.Conversation.AgentRuns.Where(item => item.Id != run.Id).ToList());
        run.MaxToolRounds = policy.MaxToolRounds;
        run.TaskComplexity = policy.Complexity.ToString();
        run.ExecutionPolicySummary = AgentExecutionPolicySummaryBuilder.Build(policy);
        return policy;
    }

    // Stage 2 of RunAsync: emit a single "GatheringContext" phase
    // change plus a Model step summarising the prompt / model /
    // tools / budget / workspace snapshot for this run. Pure
    // transformation — no I/O, no awaits beyond the IAsyncEnumerable
    // marker — so the helper exists to keep RunAsync's body
    // readable, not to share work. The output string is the same
    // shape the inline version produced (CreateContextStepOutput is
    // unchanged), and the step counter bump happens here so the
    // numbering stays contiguous with the planner step above.
    private async IAsyncEnumerable<AgentHarnessEvent> RunContextPhaseAsync(
        AgentHarnessRunRequest request,
        AgentRun run,
        AgentTaskExecutionPolicy executionPolicy)
    {
        yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.GatheringContext, "准备系统提示和会话上下文"));
        var contextStep = AddCompletedStep(
            run,
            ++_nextStepNumber,
            AgentStepType.Model,
            "准备上下文",
            request.Goal,
            CreateContextStepOutput(run, executionPolicy));
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.StepAdded,
            Run = run,
            Step = contextStep
        };
        // IAsyncEnumerable must yield — keep the compiler quiet.
        await Task.CompletedTask;
    }

    // Stage 1 of RunAsync: structured-planning phase. Caller
    // gates this behind (planner is wired in DI) AND
    // (executionPolicy.UsePlanner == true), so by the time we get
    // here `_planner` is non-null. Yields two events — a
    // Planning phase change so the UI shows what's happening
    // before the (potentially slow) planner call, then a Model
    // step with the structured plan / fallback marker once the
    // planner returns. The ModelCallCount bump lives here too
    // because the planner counts as a model call (its output
    // drives the rest of the run).
    private async IAsyncEnumerable<AgentHarnessEvent> RunPlanPhaseAsync(
        AgentHarnessRunRequest request,
        AgentRun run,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        run.PlannerUsed = true;
        run.ModelCallCount++;
        yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Planning, "生成结构化计划"));
        var planner = _planner!;
        var structuredPlan = await planner.PlanAsync(
            new AgentPlanningRequest(
                request.Goal,
                request.Context.ProjectPath,
                request.Context.EnabledToolIds,
                request.ChatRequest.Messages),
            request.Settings,
            cancellationToken);
        structuredPlan.RunId = run.Id;
        run.StructuredPlan = structuredPlan;
        run.Plan = structuredPlan.ToAgentPlan();

        var planStep = AddCompletedStep(
            run,
            ++_nextStepNumber,
            AgentStepType.Model,
            structuredPlan.IsFallback ? "生成兜底计划" : "生成结构化计划",
            request.Goal,
            CreateStructuredPlanStepOutput(structuredPlan));
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.StepAdded,
            Run = run,
            Step = planStep
        };
    }

    // Stage 3 of RunAsync: sub-agent (read-only explorer) phase.
    // Builds a schedule from the coordinator, gates it on
    // executionPolicy.AllowExplorer, and dispatches each
    // execution layer in parallel (Task.WhenAll). Per-result
    // events fire in the original layer order so the activity
    // feed / plan panel sees the same sequence as the previous
    // single-runner loop — just with the latency of the slowest
    // sub-agent in the layer instead of the sum of all of them.
    // Mutates the run record (SubAgentScheduleDecisions,
    // ExplorerDecisionReason, ExplorerUsed, SubAgentRuns) and
    // the per-instance _executionRequest (each completed
    // sub-agent's result gets appended as a synthetic system
    // message so the main model call sees the findings).
    private async IAsyncEnumerable<AgentHarnessEvent> RunSubAgentPhaseAsync(
        AgentHarnessRunRequest request,
        AgentRun run,
        AgentTaskExecutionPolicy executionPolicy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subAgentSchedule = _coordinator.CreateSubAgentSchedule(
            run.Id,
            run.StructuredPlan,
            request.ContextPack,
            request.Goal,
            run.MutationToolSucceeded);
        if (!executionPolicy.AllowExplorer)
        {
            subAgentSchedule = [];
        }
        run.SubAgentScheduleDecisions.AddRange(subAgentSchedule);
        var scheduledSubAgents = subAgentSchedule
            .Where(decision => string.Equals(decision.Status, "Scheduled", StringComparison.OrdinalIgnoreCase))
            .ToList();
        run.ExplorerDecisionReason = CreateExplorerDecisionReason(executionPolicy, subAgentSchedule, scheduledSubAgents);
        if (_subAgentScheduler is not null && scheduledSubAgents.Count > 0)
        {
            run.ExplorerUsed = true;
            // Group into execution layers so independent sub-agents
            // run in parallel (Task.WhenAll). Today every scheduled
            // decision is a read-only explorer with no intra-batch
            // dependencies, so the DAG collapses to a single layer —
            // but the algorithm is here for when worker / verifier
            // templates get wired through the same path.
            var layers = ComputeSubAgentExecutionLayers(scheduledSubAgents);
            foreach (var layer in layers)
            {
                // Emit phase changes up front so the UI shows every
                // sub-agent as "starting" before the first tool
                // event lands (matches the timing of the previous
                // single-runner loop).
                foreach (var plannedSubAgent in layer)
                {
                    yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.GatheringContext, $"运行 {plannedSubAgent.TemplateId} 子 Agent"));
                }

                // Build the request set once so the parallel dispatch
                // is a single Task.WhenAll. Each sub-agent is given
                // the shared cancellation token — the scheduler
                // returns SubAgentStatus.Cancelled rather than
                // throwing when the user hits Stop.
                var requests = layer
                    .Select(plannedSubAgent => new SubAgentRunRequest
                    {
                        ParentRunId = run.Id,
                        Task = BuildSubAgentTask(plannedSubAgent, run, request.Goal),
                        ProjectPath = request.Context.ProjectPath,
                        Settings = request.Settings,
                        TemplateId = plannedSubAgent.TemplateId,
                        ContextPack = request.ContextPack,
                        MaxToolCalls = Math.Min(plannedSubAgent.MaxToolCalls, executionPolicy.SubAgentMaxToolCalls),
                        WriteScope = plannedSubAgent.WriteScope,
                        InputArtifacts = request.Context.InputArtifacts
                    })
                    .ToList();

                SubAgentRun[] results;
                try
                {
                    results = await Task.WhenAll(requests.Select(request => _subAgentScheduler!.RunAsync(request, cancellationToken)));
                }
                catch (OperationCanceledException)
                {
                    // The whole layer was cancelled mid-flight.
                    // Synthesise a Cancelled result for every
                    // request that didn't get a chance to return
                    // one so the post-loop bookkeeping still runs.
                    results = requests
                        .Select(request => new SubAgentRun
                        {
                            ParentRunId = request.ParentRunId,
                            TemplateId = request.TemplateId,
                            Task = request.Task,
                            Status = SubAgentStatus.Cancelled
                        })
                        .ToArray();
                }

                // Emit per-result events in the original layer order
                // so the activity feed / plan panel sees the same
                // sequence as before, just with the latency of the
                // slowest sub-agent in the layer instead of the sum
                // of all of them.
                foreach (var (decision, subAgentRun) in layer.Zip(results))
                {
                    var runRecord = ToAgentSubAgentRun(subAgentRun);
                    run.SubAgentRuns.Add(runRecord);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.SubAgentStarted,
                        Run = run,
                        SubAgentRun = runRecord
                    };
                    var subAgentStep = AddCompletedStep(
                        run,
                        ++_nextStepNumber,
                        AgentStepType.Model,
                        $"{FormatTemplateName(subAgentRun.TemplateId)} 子 Agent",
                        subAgentRun.Task,
                        FormatSubAgentResult(subAgentRun));
                    RecordSubAgentArtifact(run, subAgentStep, subAgentRun);
                    _executionRequest = AppendSubAgentResultMessage(_executionRequest, subAgentRun);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.SubAgentCompleted,
                        Run = run,
                        Step = subAgentStep,
                        SubAgentRun = runRecord
                    };
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.StepAdded,
                        Run = run,
                        Step = subAgentStep
                    };
                    // 'decision' is the schedule entry that produced
                    // this run — currently unused after dispatch
                    // but kept in the loop so future per-decision
                    // hooks (artifact routing, telemetry) have a
                    // stable home.
                    _ = decision;
                }
            }
        }
    }

    // Stage 4 of RunAsync: the main tool loop. Forwards the
    // (potentially sub-agent-augmented) execution request into
    // AgentRunner.RunAsync, then translates every AgentRunEvent
    // into the corresponding AgentHarnessEvent(s) while keeping
    // per-run bookkeeping in sync. The 10-arm switch handles
    // streaming deltas, tool calls (with their approval /
    // session-allow / result fan-out), terminal states
    // (Cancelled / BudgetExceeded / Completed), and the
    // post-mutation auto-verify loop. Three of the arms
    // (Cancelled, BudgetExceeded, and the inner BudgetExceeded
    // branch of Completed) use `yield break` to stop the run
    // early — the caller's `await foreach` ends naturally when
    // the helper returns. The trailing yield (after the foreach
    // exits without a `yield break`) covers the rare case where
    // the inner runner returns without firing any of the
    // terminal events, in which case the run is marked
    // Completed with no final-step summary.
    private async IAsyncEnumerable<AgentHarnessEvent> RunToolLoopPhaseAsync(
        AgentHarnessRunRequest request,
        AgentRun run,
        AgentTaskExecutionPolicy executionPolicy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var assistantContent = "";
        var runnerReportedError = false;
        var stepByToolCallId = new Dictionary<string, AgentStep>(StringComparer.Ordinal);
        await foreach (var agentEvent in _agentRunner.RunAsync(
                           _executionRequest,
                           request.Settings,
                           ApplyExecutionPolicy(request.Context, executionPolicy),
                           cancellationToken))
        {
            switch (agentEvent.Type)
            {
                case AgentRunEventType.ModelRequestStarted:
                    run.ModelCallCount++;
                    break;
                case AgentRunEventType.RawProviderEvent:
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RawProviderEvent,
                        Run = run,
                        RawJson = agentEvent.RawJson
                    };
                    break;
                case AgentRunEventType.ContentDelta:
                    yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Summarizing, "生成最终回复"));
                    assistantContent += agentEvent.Content;
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ContentDelta,
                        Run = run,
                        Content = agentEvent.Content
                    };
                    break;
                case AgentRunEventType.ToolCall:
                    if (agentEvent.ToolCall is null)
                    {
                        break;
                    }

                    yield return CreatePhaseChanged(
                        run,
                        _coordinator.StartPhase(
                            run,
                            AgentCoordinator.ClassifyToolPhase(agentEvent.ToolCall.Name),
                            $"调用工具：{agentEvent.ToolCall.Name}"));
                    run.ToolCallCount++;
                    var step = AddRunningStep(
                        run,
                        ++_nextStepNumber,
                        AgentStepType.ToolCall,
                        $"调用工具：{agentEvent.ToolCall.Name}",
                        agentEvent.ToolCall.ArgumentsJson,
                        agentEvent.ToolCall.Id,
                        agentEvent.ToolCall.Name);
                    stepByToolCallId[agentEvent.ToolCall.Id] = step;
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ToolCall,
                        Run = run,
                        Step = step,
                        ToolCall = agentEvent.ToolCall
                    };
                    break;
                case AgentRunEventType.ToolApprovalRequired:
                    run.ToolApprovalRequiredCount++;
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ToolApprovalRequired,
                        Run = run,
                        ToolCall = agentEvent.ToolCall,
                        ToolPreview = agentEvent.ToolPreview
                    };
                    break;
                case AgentRunEventType.ToolApprovalRejected:
                    run.ToolApprovalRejectedCount++;
                    CompleteToolStep(stepByToolCallId, agentEvent.ToolCall, "用户拒绝执行该工具。", isError: true);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ToolApprovalRejected,
                        Run = run,
                        ToolCall = agentEvent.ToolCall,
                        ToolPreview = agentEvent.ToolPreview
                    };
                    break;
                case AgentRunEventType.ToolSessionAllowed:
                    run.ToolSessionAllowedCount++;
                    break;
                case AgentRunEventType.ToolResult:
                    if (agentEvent.ToolResult is not null)
                    {
                        CompleteToolStep(
                            stepByToolCallId,
                            agentEvent.ToolCall,
                            agentEvent.ToolResult.Content,
                            agentEvent.ToolResult.IsError);
                        RecordFileChanges(
                            run,
                            stepByToolCallId,
                            agentEvent.ToolCall,
                            agentEvent.ToolPreview,
                            agentEvent.ToolResult);
                        RecordVerification(
                            run,
                            stepByToolCallId,
                            agentEvent.ToolCall,
                            agentEvent.ToolResult);
                        RecordMutationGuardrail(
                            run,
                            agentEvent.ToolCall,
                            agentEvent.ToolPreview,
                            agentEvent.ToolResult);
                        RecordPlan(run, agentEvent.ToolCall, agentEvent.ToolResult);
                        RecordArtifact(
                            run,
                            stepByToolCallId,
                            agentEvent.ToolCall,
                            agentEvent.ToolResult);
                    }

                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ToolResult,
                        Run = run,
                        Step = agentEvent.ToolCall is not null && stepByToolCallId.TryGetValue(agentEvent.ToolCall.Id, out var toolStep)
                            ? toolStep
                            : null,
                        ToolCall = agentEvent.ToolCall,
                        ToolResult = agentEvent.ToolResult
                    };
                    break;
                case AgentRunEventType.Error:
                    runnerReportedError = true;
                    run.CompletionReason = SensitiveDataRedactor.RedactText(agentEvent.Content);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ContentDelta,
                        Run = run,
                        Content = agentEvent.Content
                    };
                    break;
                case AgentRunEventType.Cancelled:
                {
                    var reason = string.IsNullOrWhiteSpace(agentEvent.Content)
                        ? "Agent 运行已取消。"
                        : agentEvent.Content;
                    await foreach (var evt in EmitTerminalAsync(run, reason, "运行已取消", AgentRunStatus.Cancelled, isBudgetExceeded: false))
                    {
                        yield return evt;
                    }
                    yield break;
                }
                case AgentRunEventType.BudgetExceeded:
                {
                    await foreach (var evt in EmitTerminalAsync(run, "工具预算已用完，任务已暂停。", "预算暂停", AgentRunStatus.BudgetExceeded, isBudgetExceeded: true))
                    {
                        yield return evt;
                    }
                    yield break;
                }
                case AgentRunEventType.Completed:
                    // Auto-verify loop: after mutations, run verification commands
                    // and feed failures back to the model for another fix round.
                    if (request.Context.AutoVerifyAgentRuns &&
                        request.Context.VerificationCommands.Count > 0 &&
                        run.MutationToolSucceeded)
                    {
                        yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Verifying, "运行自动验证"));
                        await foreach (var verifyEvent in RunAutoVerifyLoopAsync(
                                           run, request, stepByToolCallId,
                                           request.Settings, ApplyExecutionPolicy(request.Context, executionPolicy), cancellationToken))
                        {
                            yield return verifyEvent;
                        }

                        if (run.ToolBudgetExceeded)
                        {
                            await foreach (var evt in EmitTerminalAsync(run, "工具预算已用完，任务已暂停。", "预算暂停", AgentRunStatus.BudgetExceeded, isBudgetExceeded: true))
                            {
                                yield return evt;
                            }
                            yield break;
                        }
                    }

                    var finalStatus = runnerReportedError ? AgentRunStatus.Failed : DetermineFinalStatus(run);
                    yield return CreatePhaseChanged(run, CompleteRun(run, finalStatus));
                    // Use steps count to derive the final step number; the auto-verify
                    // loop may have added intermediate steps that overflow _nextStepNumber.
                    var finalStep = AddCompletedStep(
                        run,
                        run.Steps.Count + 1,
                        AgentStepType.Final,
                        "生成最终回复",
                        "",
                        assistantContent);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RunCompleted,
                        Run = run,
                        Step = finalStep
                    };
                    yield break;
            }
        }

        // Runner returned without firing Completed / Cancelled /
        // BudgetExceeded — treat the run as Completed with no
        // final-step summary. Same semantics as the trailing
        // yield in the pre-extraction RunAsync.
        yield return CreatePhaseChanged(run, CompleteRun(run, AgentRunStatus.Completed));
    }

    private static AgentRunStatus DetermineFinalStatus(AgentRun run)
    {
        if (run.ToolBudgetExceeded)
        {
            return AgentRunStatus.BudgetExceeded;
        }

        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            return AgentRunStatus.Failed;
        }

        return AgentRunStatus.Completed;
    }

    private static string CreateExplorerDecisionReason(
        AgentTaskExecutionPolicy policy,
        IReadOnlyList<AgentSubAgentScheduleDecision> schedule,
        IReadOnlyList<AgentSubAgentScheduleDecision> scheduled)
    {
        if (!policy.AllowExplorer)
        {
            return "Explorer skipped by execution policy.";
        }

        if (scheduled.Count > 0)
        {
            return $"Explorer scheduled: {scheduled.Count}.";
        }

        if (schedule.Count > 0)
        {
            var skipped = schedule.FirstOrDefault(decision => !string.IsNullOrWhiteSpace(decision.SkipReason));
            return string.IsNullOrWhiteSpace(skipped?.SkipReason)
                ? "Explorer not scheduled after coordinator filtering."
                : "Explorer skipped: " + skipped.SkipReason;
        }

        return "Explorer allowed but no schedule was produced.";
    }

    // Inline of the previous AgentStrategyAdvisor.Adjust: the
    // "should I bump MaxToolRounds because the user keeps hitting
    // the tool budget" decision lives here so the only collaborator
    // of AgentHarness stays AgentTaskExecutionPolicyBuilder. The
    // logic is the same as the deleted AgentStrategyAdvisor —
    // when adaptive flags are off, the policy is returned as-is.
    private static AgentTaskExecutionPolicy ApplyHistoricalAdjustments(
        AgentTaskExecutionPolicy policy,
        AgentRunContext context,
        IReadOnlyList<AgentRun> history)
    {
        if (history.Count == 0)
        {
            return policy;
        }

        var notes = new List<string>();
        var adjusted = policy;

        if (context.AdaptiveStrategiesEnabled && context.AdaptiveBudgetAndExplorerEnabled)
        {
            var baseLimit = Math.Max(1, context.MaxToolRounds);
            var recentSameComplexity = history
                .Where(run => string.Equals(run.TaskComplexity, policy.Complexity.ToString(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(run => run.StartedAt)
                .Take(12)
                .ToList();
            var budgetExceededCount = recentSameComplexity
                .Count(run => run.ToolBudgetExceeded || run.Status == AgentRunStatus.BudgetExceeded);

            if (budgetExceededCount >= 2 && policy.MaxToolRounds < baseLimit)
            {
                var extraBudget = policy.Complexity == AgentTaskComplexity.Simple ? 2 : 6;
                var newBudget = Math.Min(baseLimit, policy.MaxToolRounds + extraBudget);
                if (newBudget > policy.MaxToolRounds)
                {
                    adjusted = adjusted with { MaxToolRounds = newBudget };
                    notes.Add($"recent budget pressure: {policy.MaxToolRounds}->{newBudget}");
                }
            }
        }

        return notes.Count == 0 ? policy : adjusted with { StrategyAdjustment = string.Join("; ", notes) };
    }

    private static AgentRunContext ApplyExecutionPolicy(
        AgentRunContext context,
        AgentTaskExecutionPolicy policy)
    {
        var autoVerify = context.AutoVerifyAgentRuns || policy.ForceAutoVerifyAfterMutation;
        if (context.MaxToolRounds == policy.MaxToolRounds &&
            context.AutoVerifyAgentRuns == autoVerify)
        {
            return context;
        }

        return new AgentRunContext
        {
            ProjectPath = context.ProjectPath,
            EnabledToolIds = context.EnabledToolIds,
            ToolPermissionModes = context.ToolPermissionModes,
            RequestToolApprovalAsync = context.RequestToolApprovalAsync,
            MaxToolRounds = policy.MaxToolRounds,
            ProjectPreparationSucceeded = context.ProjectPreparationSucceeded,
            ProjectPreparationSummary = context.ProjectPreparationSummary,
            ProjectAgentsAvailable = context.ProjectAgentsAvailable,
            ProjectVerificationCommandCount = context.ProjectVerificationCommandCount,
            AutoVerifyAgentRuns = autoVerify,
            MaxAutoFixRounds = context.MaxAutoFixRounds,
            AdaptiveStrategiesEnabled = context.AdaptiveStrategiesEnabled,
            AdaptiveBudgetAndExplorerEnabled = context.AdaptiveBudgetAndExplorerEnabled,
            VerificationCommands = context.VerificationCommands,
            InputArtifacts = context.InputArtifacts
        };
    }

    private static void RecordMutationGuardrail(
        AgentRun run,
        ChatToolCall? toolCall,
        AgentToolPreview? preview,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            toolResult.IsError ||
            preview?.Risk == AgentToolRisk.ReadOnly ||
            !IsMutationTool(toolResult.ToolName))
        {
            return;
        }

        run.MutationToolSucceeded = true;
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private static bool IsMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit";
    }

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

    private static AgentStep AddRunningStep(
        AgentRun run,
        int number,
        AgentStepType type,
        string title,
        string input,
        string toolCallId = "",
        string toolName = "")
    {
        var step = new AgentStep
        {
            RunId = run.Id,
            Number = number,
            Type = type,
            Title = title,
            Input = SensitiveDataRedactor.RedactText(input),
            ToolCallId = toolCallId,
            ToolName = toolName,
            StartedAt = DateTimeOffset.Now
        };
        run.Steps.Add(step);
        return step;
    }

    private static AgentStep AddCompletedStep(
        AgentRun run,
        int number,
        AgentStepType type,
        string title,
        string input,
        string output)
    {
        var now = DateTimeOffset.Now;
        var step = new AgentStep
        {
            RunId = run.Id,
            Number = number,
            Type = type,
            Status = AgentStepStatus.Completed,
            Title = title,
            Input = SensitiveDataRedactor.RedactText(input),
            Output = SensitiveDataRedactor.RedactText(output),
            StartedAt = now,
            CompletedAt = now
        };
        run.Steps.Add(step);
        return step;
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

    private static void CompleteToolStep(
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        string output,
        bool isError)
    {
        if (toolCall is null || !stepByToolCallId.TryGetValue(toolCall.Id, out var step))
        {
            return;
        }

        step.Output = SensitiveDataRedactor.RedactText(output);
        step.IsError = isError;
        step.Status = isError ? AgentStepStatus.Failed : AgentStepStatus.Completed;
        step.CompletedAt = DateTimeOffset.Now;
    }

    private static void RecordFileChanges(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolPreview? preview,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            toolResult.IsError ||
            preview is null ||
            preview.Risk == AgentToolRisk.ReadOnly ||
            string.IsNullOrWhiteSpace(preview.DiffText))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";

        foreach (var changedFile in ParseChangedFiles(toolResult.Content))
        {
            run.FileChanges.Add(new AgentFileChange
            {
                RunId = run.Id,
                StepId = stepId,
                ToolCallId = toolCall.Id,
                ToolName = toolResult.ToolName,
                Path = changedFile.Path,
                DiffText = SensitiveDataRedactor.RedactText(ExtractDiffForPath(preview.DiffText, changedFile.Path)),
                OldChars = changedFile.OldChars,
                NewChars = changedFile.NewChars,
                ContentSnapshot = changedFile.ContentSnapshot,
                PostChangeHash = changedFile.PostChangeHash,
                CreatedAt = DateTimeOffset.Now
            });
        }
    }

    private static void RecordVerification(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !IsVerificationTool(toolResult.ToolName))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";
        var parsed = ParseVerification(toolResult);
        var safeOutput = SensitiveDataRedactor.RedactText(parsed.Output);
        var isSuccess = !toolResult.IsError && parsed.ExitCode == 0 && !parsed.TimedOut;
        run.Verifications.Add(new AgentVerification
        {
            RunId = run.Id,
            StepId = stepId,
            ToolCallId = toolCall.Id,
            ToolName = toolResult.ToolName,
            Command = parsed.Command,
            ExitCode = parsed.ExitCode,
            TimedOut = parsed.TimedOut,
            IsSuccess = isSuccess,
            Output = safeOutput,
            Summary = VerificationResultParser.Summarize(safeOutput),
            CreatedAt = DateTimeOffset.Now
        });
    }

    private static void RecordArtifact(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !toolResult.WasSummarized ||
            string.IsNullOrWhiteSpace(toolResult.Content) ||
            run.Artifacts.Any(artifact => string.Equals(artifact.ToolCallId, toolCall.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";

        run.Artifacts.Add(new AgentArtifact
        {
            RunId = run.Id,
            StepId = stepId,
            ToolCallId = toolCall.Id,
            ToolName = toolResult.ToolName,
            Kind = string.IsNullOrWhiteSpace(toolResult.ArtifactKind) ? "tool_result" : toolResult.ArtifactKind,
            Summary = SensitiveDataRedactor.RedactText(toolResult.Summary),
            Content = SensitiveDataRedactor.RedactText(toolResult.Content),
            CreatedAt = DateTimeOffset.Now,
            Metadata =
            {
                ["contentLength"] = toolResult.Content.Length.ToString(),
                ["modelContentLength"] = toolResult.ContentForModel.Length.ToString(),
                ["wasSummarized"] = "true"
            }
        });
    }

    private static bool IsVerificationTool(string toolName)
    {
        return string.Equals(toolName, "run_build", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "run_test", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "run_shell", StringComparison.OrdinalIgnoreCase);
    }

    private async IAsyncEnumerable<AgentHarnessEvent> RunAutoVerifyLoopAsync(
        AgentRun run,
        AgentHarnessRunRequest request,
        Dictionary<string, AgentStep> stepByToolCallId,
        AppSettings settings,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxRounds = context.MaxAutoFixRounds;
        for (var round = 0; round < maxRounds; round++)
        {
            var allPassed = true;
            var failureMessages = new List<string>();

            foreach (var cmd in context.VerificationCommands)
            {
                var toolName = GetVerificationToolName(cmd.Command);
                var tool = CreateVerificationTool(toolName);
                if (tool is null)
                {
                    continue;
                }

                var toolCallId = $"auto-verify-{run.Id}-{round}-{cmd.Id}";
                var argsJson = BuildVerificationArgsJson(cmd, toolName);
                var preview = await tool.PreviewAsync(argsJson, CreateToolContext(context), cancellationToken);

                var step = AddRunningStep(
                    run,
                    run.Steps.Count + 1,
                    AgentStepType.ToolCall,
                    $"自动验证：{cmd.Name}",
                    argsJson,
                    toolCallId,
                    toolName);
                stepByToolCallId[toolCallId] = step;
                yield return new AgentHarnessEvent
                {
                    Type = AgentHarnessEventType.ToolCall,
                    Run = run,
                    Step = step,
                    ToolCall = new ChatToolCall { Id = toolCallId, Name = toolName, ArgumentsJson = argsJson }
                };

                var result = ToolResultSummarizer.Summarize(
                    await tool.ExecuteAsync(argsJson, CreateToolContext(context), cancellationToken));
                var verifyToolCall = new ChatToolCall { Id = toolCallId, Name = toolName, ArgumentsJson = argsJson };
                CompleteToolStep(stepByToolCallId, verifyToolCall, result.Content, result.IsError);
                RecordVerification(run, stepByToolCallId, verifyToolCall, result);
                RecordArtifact(run, stepByToolCallId, verifyToolCall, result);
                run.ToolCallCount++;

                var verification = run.Verifications.LastOrDefault(v => v.ToolCallId == toolCallId);
                if (verification is not null && !verification.IsSuccess)
                {
                    allPassed = false;
                    var summary = string.IsNullOrWhiteSpace(verification.Summary)
                        ? verification.Output.Length > 500 ? verification.Output[..500] + "..." : verification.Output
                        : verification.Summary;
                    failureMessages.Add($"[{cmd.Name}] exit {verification.ExitCode}:\n{summary}");
                }

                yield return new AgentHarnessEvent
                {
                    Type = AgentHarnessEventType.ToolResult,
                    Run = run,
                    Step = step,
                    ToolCall = verifyToolCall,
                    ToolResult = result
                };
            }

            if (allPassed)
            {
                yield break;
            }

            // Feed failure summary back to the model for another fix round
            var failureSummary = "自动验证失败，请修复后重试：\n\n" + string.Join("\n\n", failureMessages);
            yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Repairing, "自动验证失败，进入修复阶段"));
            var repairPrompt = _promptComposer.Compose(new AgentPromptComposeRequest
            {
                Profile = AgentPromptProfile.VerificationRepair,
                Goal = request.Goal,
                Plan = run.StructuredPlan,
                Budget = run.StructuredPlan?.Budget,
                AllowedTools = context.EnabledToolIds,
                FailureSummary = failureSummary,
                ResponseRequirements = "修复后简要说明改动和验证结果。"
            });
            var updatedMessages = new List<ChatMessage>(request.ChatRequest.Messages);
            updatedMessages.AddRange(repairPrompt.Messages);
            var updatedRequest = new ChatRequest
            {
                Model = request.ChatRequest.Model,
                Temperature = request.ChatRequest.Temperature,
                Messages = updatedMessages
            };
            request = request with { ChatRequest = updatedRequest };

            await foreach (var fixEvent in _agentRunner.RunAsync(
                               updatedRequest,
                               settings,
                               context,
                               cancellationToken))
            {
                switch (fixEvent.Type)
                {
                    case AgentRunEventType.ModelRequestStarted:
                        run.ModelCallCount++;
                        break;
                    case AgentRunEventType.RawProviderEvent:
                        yield return new AgentHarnessEvent
                        {
                            Type = AgentHarnessEventType.RawProviderEvent,
                            Run = run,
                            RawJson = fixEvent.RawJson
                        };
                        break;
                    case AgentRunEventType.ContentDelta:
                        yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Summarizing, "生成修复说明"));
                        yield return new AgentHarnessEvent
                        {
                            Type = AgentHarnessEventType.ContentDelta,
                            Run = run,
                            Content = fixEvent.Content
                        };
                        break;
                    case AgentRunEventType.ToolCall:
                        if (fixEvent.ToolCall is not null)
                        {
                            yield return CreatePhaseChanged(
                                run,
                                _coordinator.StartPhase(
                                    run,
                                    AgentCoordinator.ClassifyToolPhase(fixEvent.ToolCall.Name),
                                    $"修复阶段调用工具：{fixEvent.ToolCall.Name}"));
                            run.ToolCallCount++;
                            var fixStep = AddRunningStep(
                                run,
                                run.Steps.Count + 1,
                                AgentStepType.ToolCall,
                                $"调用工具：{fixEvent.ToolCall.Name}",
                                fixEvent.ToolCall.ArgumentsJson,
                                fixEvent.ToolCall.Id,
                                fixEvent.ToolCall.Name);
                            stepByToolCallId[fixEvent.ToolCall.Id] = fixStep;
                            yield return new AgentHarnessEvent
                            {
                                Type = AgentHarnessEventType.ToolCall,
                                Run = run,
                                Step = fixStep,
                                ToolCall = fixEvent.ToolCall
                            };
                        }

                        break;
                    case AgentRunEventType.ToolResult:
                        if (fixEvent.ToolResult is not null)
                        {
                            CompleteToolStep(
                                stepByToolCallId,
                                fixEvent.ToolCall,
                                fixEvent.ToolResult.Content,
                                fixEvent.ToolResult.IsError);
                            RecordFileChanges(
                                run,
                                stepByToolCallId,
                                fixEvent.ToolCall,
                                fixEvent.ToolPreview,
                                fixEvent.ToolResult);
                            RecordVerification(
                                run,
                                stepByToolCallId,
                                fixEvent.ToolCall,
                                fixEvent.ToolResult);
                            RecordMutationGuardrail(
                                run,
                                fixEvent.ToolCall,
                                fixEvent.ToolPreview,
                                fixEvent.ToolResult);
                            RecordArtifact(
                                run,
                                stepByToolCallId,
                                fixEvent.ToolCall,
                                fixEvent.ToolResult);
                        }

                        yield return new AgentHarnessEvent
                        {
                            Type = AgentHarnessEventType.ToolResult,
                            Run = run,
                            Step = fixEvent.ToolCall is not null && stepByToolCallId.TryGetValue(fixEvent.ToolCall.Id, out var s) ? s : null,
                            ToolCall = fixEvent.ToolCall,
                            ToolResult = fixEvent.ToolResult
                        };
                        break;
                    case AgentRunEventType.BudgetExceeded:
                        run.ToolBudgetExceeded = true;
                        run.CompletionReason = "自动修复阶段已达到工具调用轮数上限。";
                        yield break;
                    case AgentRunEventType.Cancelled:
                        run.CompletionReason = string.IsNullOrWhiteSpace(fixEvent.Content)
                            ? "自动修复阶段已取消。"
                            : SensitiveDataRedactor.RedactText(fixEvent.Content);
                        yield break;
                    case AgentRunEventType.Error:
                        run.CompletionReason = string.IsNullOrWhiteSpace(fixEvent.Content)
                            ? "自动修复阶段失败。"
                            : SensitiveDataRedactor.RedactText(fixEvent.Content);
                        yield break;
                    case AgentRunEventType.Completed:
                        // Inner runner completed — break to continue auto-verify loop
                        goto nextRound;
                }
            }

            nextRound: ;
        }
    }

    private static string GetVerificationToolName(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        if (string.Equals(normalized, "dotnet test", StringComparison.Ordinal))
        {
            return "run_test";
        }

        if (string.Equals(normalized, "dotnet build", StringComparison.Ordinal))
        {
            return "run_build";
        }

        return ShellCommandTool.IsAllowlisted(command) ? "run_shell" : "";
    }

    private static IAgentTool? CreateVerificationTool(string toolName)
    {
        return toolName switch
        {
            "run_build" => new RunBuildTool(),
            "run_test" => new RunTestTool(),
            "run_shell" => new ShellCommandTool(),
            _ => null
        };
    }

    private static string BuildVerificationArgsJson(Domain.Projects.ProjectVerificationCommand cmd, string toolName)
    {
        if (string.Equals(toolName, "run_shell", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                command = cmd.Command,
                shell = OperatingSystem.IsWindows() ? "cmd" : "auto",
                working_directory = cmd.WorkingDirectory,
                timeout_seconds = cmd.TimeoutSeconds > 0 ? cmd.TimeoutSeconds : 120,
                max_output_chars = 20_000
            });
        }

        return BuildDotnetVerificationArgsJson(cmd);
    }

    private static string BuildDotnetVerificationArgsJson(Domain.Projects.ProjectVerificationCommand cmd)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        var first = true;
        if (!string.IsNullOrWhiteSpace(cmd.WorkingDirectory))
        {
            sb.Append($"\"target\":\"{EscapeJson(cmd.WorkingDirectory)}\"");
            first = false;
        }

        if (cmd.TimeoutSeconds > 0 && cmd.TimeoutSeconds != 120)
        {
            if (!first) sb.Append(',');
            sb.Append($"\"timeout_seconds\":{cmd.TimeoutSeconds}");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static AgentToolContext CreateToolContext(AgentRunContext context)
    {
        return new AgentToolContext
        {
            ProjectPath = context.ProjectPath,
            InputArtifacts = context.InputArtifacts
        };
    }


    private static void RecordPlan(AgentRun run, ChatToolCall? toolCall, AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !string.Equals(toolCall.Name, "update_plan", StringComparison.OrdinalIgnoreCase) ||
            toolResult.IsError)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(toolResult.Content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var summary = root.TryGetProperty("summary", out var summaryElement) &&
                          summaryElement.ValueKind == JsonValueKind.String
                ? summaryElement.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(summary) && !root.TryGetProperty("itemCount", out _))
            {
                return;
            }

            // Parse items from the original tool call arguments, not the result
            var planItems = new List<AgentPlanItem>();
            try
            {
                using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                var argsRoot = argsDoc.RootElement;
                if (argsRoot.TryGetProperty("items", out var itemsElement) &&
                    itemsElement.ValueKind == JsonValueKind.Array)
                {
                    var order = 0;
                    foreach (var itemElement in itemsElement.EnumerateArray())
                    {
                        var title = itemElement.TryGetProperty("title", out var titleElement) &&
                                    titleElement.ValueKind == JsonValueKind.String
                            ? titleElement.GetString() ?? ""
                            : "";

                        if (string.IsNullOrWhiteSpace(title))
                        {
                            continue;
                        }

                        var statusText = itemElement.TryGetProperty("status", out var statusElement) &&
                                         statusElement.ValueKind == JsonValueKind.String
                            ? statusElement.GetString() ?? ""
                            : "";

                        var notes = itemElement.TryGetProperty("notes", out var notesElement) &&
                                    notesElement.ValueKind == JsonValueKind.String
                            ? notesElement.GetString() ?? ""
                            : "";

                        planItems.Add(new AgentPlanItem
                        {
                            Title = title,
                            Status = UpdatePlanTool.ParseStatus(statusText),
                            Notes = notes,
                            Order = order++
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // If arguments can't be parsed, still update the summary
            }

            if (run.Plan is null)
            {
                run.Plan = new AgentPlan
                {
                    RunId = run.Id,
                    Summary = summary,
                    Items = planItems,
                    CreatedAt = DateTimeOffset.Now,
                    UpdatedAt = DateTimeOffset.Now
                };
            }
            else
            {
                run.Plan.Summary = summary;
                run.Plan.UpdatedAt = DateTimeOffset.Now;

                // Match existing items by title, update status/notes; add new items
                var existingByTitle = run.Plan.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                    .ToDictionary(item => item.Title, item => item, StringComparer.OrdinalIgnoreCase);

                foreach (var newItem in planItems)
                {
                    if (existingByTitle.TryGetValue(newItem.Title, out var existing))
                    {
                        existing.Status = newItem.Status;
                        existing.Notes = newItem.Notes;
                    }
                    else
                    {
                        newItem.Order = run.Plan.Items.Count;
                        run.Plan.Items.Add(newItem);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed plan update results
        }
    }

    private static VerificationInfo ParseVerification(AgentToolResult toolResult)
    {
        try
        {
            using var document = JsonDocument.Parse(toolResult.Content);
            var root = document.RootElement;
            return new VerificationInfo(
                GetString(root, "command"),
                GetInt(root, "exitCode", toolResult.IsError ? 1 : 0),
                GetBool(root, "timedOut"),
                GetString(root, "output"));
        }
        catch (JsonException)
        {
            return new VerificationInfo(toolResult.ToolName, toolResult.IsError ? 1 : 0, false, toolResult.Content);
        }
    }

    private static string ExtractDiffForPath(string diffText, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var expectedHeader = $"--- a/{normalizedPath}";
        var lines = diffText.ReplaceLineEndings("\n").Split('\n');
        var current = new List<string>();
        var isCurrentMatch = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                if (isCurrentMatch && current.Count > 0)
                {
                    return string.Join(Environment.NewLine, current).TrimEnd();
                }

                current.Clear();
                isCurrentMatch = string.Equals(line, expectedHeader, StringComparison.OrdinalIgnoreCase);
            }

            if (current.Count > 0 || line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                current.Add(line);
            }
        }

        return isCurrentMatch && current.Count > 0
            ? string.Join(Environment.NewLine, current).TrimEnd()
            : diffText;
    }

    private static IReadOnlyList<ChangedFileInfo> ParseChangedFiles(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (root.TryGetProperty("changedFiles", out var changedFiles) &&
                changedFiles.ValueKind == JsonValueKind.Array)
            {
                return changedFiles
                    .EnumerateArray()
                    .Select(ParseChangedFile)
                    .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                    .ToList();
            }

            var single = ParseChangedFile(root);
            return string.IsNullOrWhiteSpace(single.Path) ? [] : [single];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ChangedFileInfo ParseChangedFile(JsonElement element)
    {
        return new ChangedFileInfo(
            GetString(element, "path"),
            GetInt(element, "oldChars"),
            GetInt(element, "newChars", GetInt(element, "chars")),
            GetString(element, "contentSnapshot"),
            GetString(element, "postChangeHash"));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : defaultValue;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }

    private static AgentHarnessEvent CreatePhaseChanged(AgentRun run, AgentPhaseTransition transition)
    {
        return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.PhaseChanged,
            Run = run,
            PhaseTransition = transition
        };
    }

    private AgentPhaseTransition CompleteRun(AgentRun run, AgentRunStatus status)
    {
        return _coordinator.CompleteRun(run, status, run.CompletionReason);
    }

    private sealed record ChangedFileInfo(string Path, int OldChars, int NewChars, string ContentSnapshot, string PostChangeHash);
    private sealed record VerificationInfo(string Command, int ExitCode, bool TimedOut, string Output);
}
