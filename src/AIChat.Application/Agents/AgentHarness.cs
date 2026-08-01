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
// Split across partial files so the orchestration file stays focused:
//   - AgentHarness.cs              — ctor, fields, RunAsync, phase methods
//   - AgentHarness.AutoVerify.cs   — auto-verify tool resolution helpers
//   - AgentHarness.Recording.cs    — Record*/Add*/Complete* step bookkeeping
//   - AgentHarness.SubAgent.cs     — sub-agent / plan / context formatters
public sealed partial class AgentHarness
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

}
