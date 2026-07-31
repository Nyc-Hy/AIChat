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
    private readonly AgentCompletionEvidenceChecker _completionEvidenceChecker;
    private readonly AgentRunQualityEvaluator _qualityEvaluator;
    private readonly AgentStrategyAdvisor _strategyAdvisor;

    public AgentHarness(
        AgentRunner agentRunner,
        AgentPlanner? planner = null,
        AgentCoordinator? coordinator = null,
        AgentPromptComposer? promptComposer = null,
        SubAgentScheduler? subAgentScheduler = null,
        AgentTaskClassifier? taskClassifier = null,
        AgentTaskExecutionPolicyBuilder? executionPolicyBuilder = null,
        AgentCompletionEvidenceChecker? completionEvidenceChecker = null,
        AgentRunQualityEvaluator? qualityEvaluator = null,
        AgentStrategyAdvisor? strategyAdvisor = null)
    {
        _agentRunner = agentRunner;
        _planner = planner;
        _coordinator = coordinator ?? new AgentCoordinator();
        _promptComposer = promptComposer ?? new AgentPromptComposer();
        _subAgentScheduler = subAgentScheduler;
        _taskClassifier = taskClassifier ?? new AgentTaskClassifier();
        _executionPolicyBuilder = executionPolicyBuilder ?? new AgentTaskExecutionPolicyBuilder();
        _completionEvidenceChecker = completionEvidenceChecker ?? new AgentCompletionEvidenceChecker();
        _qualityEvaluator = qualityEvaluator ?? new AgentRunQualityEvaluator();
        _strategyAdvisor = strategyAdvisor ?? new AgentStrategyAdvisor();
    }

    public async IAsyncEnumerable<AgentHarnessEvent> RunAsync(
        AgentHarnessRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
                entry => entry.Key,
                entry => entry.Value.ToString(),
                StringComparer.OrdinalIgnoreCase),
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
        var taskComplexity = _taskClassifier.Classify(request.Goal, request.Context);
        var executionPolicy = _executionPolicyBuilder.Build(
            taskComplexity,
            request.Context,
            request.ContextPack,
            !string.IsNullOrWhiteSpace(request.ContinuedFromRunId));
        executionPolicy = _strategyAdvisor.Adjust(
            executionPolicy,
            request.Context,
            request.Conversation.AgentRuns.Where(item => item.Id != run.Id).ToList());
        run.MaxToolRounds = executionPolicy.MaxToolRounds;
        run.TaskComplexity = executionPolicy.Complexity.ToString();
        run.ExecutionPolicySummary = AgentExecutionPolicySummaryBuilder.Build(executionPolicy);
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.RunStarted,
            Run = run
        };

        var stepNumber = 0;
        var planner = _planner;
        var shouldPlan = planner is not null && executionPolicy.UsePlanner;
        if (shouldPlan)
        {
            run.PlannerUsed = true;
            run.ModelCallCount++;
            yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Planning, "生成结构化计划"));
            var structuredPlan = await planner!.PlanAsync(
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
                ++stepNumber,
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

        yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.GatheringContext, "准备系统提示和会话上下文"));
        var contextStep = AddCompletedStep(
            run,
            ++stepNumber,
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

        var executionRequest = request.ChatRequest;
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
                        ++stepNumber,
                        AgentStepType.Model,
                        $"{FormatTemplateName(subAgentRun.TemplateId)} 子 Agent",
                        subAgentRun.Task,
                        FormatSubAgentResult(subAgentRun));
                    RecordSubAgentArtifact(run, subAgentStep, subAgentRun);
                    executionRequest = AppendSubAgentResultMessage(executionRequest, subAgentRun);
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

        var assistantContent = "";
        var runnerReportedError = false;
        var stepByToolCallId = new Dictionary<string, AgentStep>(StringComparer.Ordinal);
        await foreach (var agentEvent in _agentRunner.RunAsync(
                           executionRequest,
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
                        ++stepNumber,
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
                    run.CompletionReason = SensitiveDataRedactor.RedactText(reason);
                    CompleteFinalValidation(run, assistantContent);
                    run.FinalStatusReason = CreateFinalStatusReason(run, AgentRunStatus.Cancelled);
                    CompleteQualityAssessment(run, AgentRunStatus.Cancelled);
                    CompleteRecoverySuggestion(run, executionPolicy, AgentRunStatus.Cancelled);
                    CompleteTelemetry(run);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ContentDelta,
                        Run = run,
                        Content = reason
                    };
                    yield return CreatePhaseChanged(run, CompleteRun(run, AgentRunStatus.Cancelled));
                    var cancelledStep = AddCompletedStep(
                        run,
                        run.Steps.Count + 1,
                        AgentStepType.Final,
                        "运行已取消",
                        "",
                        reason);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RunCompleted,
                        Run = run,
                        Step = cancelledStep
                    };
                    yield break;
                }
                case AgentRunEventType.BudgetExceeded:
                {
                    run.ToolBudgetExceeded = true;
                    run.CompletionReason = "已达到工具调用轮数上限。";
                    CompleteFinalValidation(run, assistantContent);
                    run.FinalStatusReason = CreateFinalStatusReason(run, AgentRunStatus.BudgetExceeded);
                    CompleteQualityAssessment(run, AgentRunStatus.BudgetExceeded);
                    CompleteRecoverySuggestion(run, executionPolicy);
                    CompleteTelemetry(run);
                    var budgetMessage = CreateBudgetPausedUserMessage(run);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ContentDelta,
                        Run = run,
                        Content = budgetMessage
                    };
                    yield return CreatePhaseChanged(run, CompleteRun(run, AgentRunStatus.BudgetExceeded));
                    var budgetStep = AddCompletedStep(
                        run,
                        run.Steps.Count + 1,
                        AgentStepType.Final,
                        "预算暂停",
                        "",
                        budgetMessage);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RunCompleted,
                        Run = run,
                        Step = budgetStep
                    };
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
                            CompleteFinalValidation(run, assistantContent);
                            run.FinalStatusReason = CreateFinalStatusReason(run, AgentRunStatus.BudgetExceeded);
                            CompleteQualityAssessment(run, AgentRunStatus.BudgetExceeded);
                            CompleteRecoverySuggestion(run, executionPolicy);
                            CompleteTelemetry(run);
                            var budgetMessage = CreateBudgetPausedUserMessage(run);
                            yield return new AgentHarnessEvent
                            {
                                Type = AgentHarnessEventType.ContentDelta,
                                Run = run,
                                Content = budgetMessage
                            };
                            yield return CreatePhaseChanged(run, CompleteRun(run, AgentRunStatus.BudgetExceeded));
                            var budgetStep = AddCompletedStep(
                                run,
                                run.Steps.Count + 1,
                                AgentStepType.Final,
                                "预算暂停",
                                "",
                                budgetMessage);
                            yield return new AgentHarnessEvent
                            {
                                Type = AgentHarnessEventType.RunCompleted,
                                Run = run,
                                Step = budgetStep
                            };
                            yield break;
                        }
                    }

                    CompleteFinalValidation(run, assistantContent);
                    var finalStatus = runnerReportedError ? AgentRunStatus.Failed : DetermineFinalStatus(run);
                    run.FinalStatusReason = CreateFinalStatusReason(run, finalStatus);
                    CompleteQualityAssessment(run, finalStatus);
                    CompleteRecoverySuggestion(run, executionPolicy);
                    CompleteTelemetry(run);
                    var finalContent = BuildFinalContent(assistantContent, run, finalStatus);
                    yield return CreatePhaseChanged(run, _coordinator.StartPhase(run, AgentRunPhase.Summarizing, "生成最终回复"));
                    var finalAppendix = CreateFinalContentAppendix(run, finalStatus);
                    if (!string.IsNullOrWhiteSpace(finalAppendix))
                    {
                        yield return new AgentHarnessEvent
                        {
                            Type = AgentHarnessEventType.ContentDelta,
                            Run = run,
                            Content = string.IsNullOrWhiteSpace(assistantContent)
                                ? finalAppendix
                                : Environment.NewLine + Environment.NewLine + finalAppendix
                        };
                    }

                    yield return CreatePhaseChanged(run, CompleteRun(run, finalStatus));
                    // Use steps count to derive the final step number; the auto-verify
                    // loop may have added intermediate steps that overflow stepNumber.
                    var finalStep = AddCompletedStep(
                        run,
                        run.Steps.Count + 1,
                        AgentStepType.Final,
                        "生成最终回复",
                        "",
                        finalContent);
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RunCompleted,
                        Run = run,
                        Step = finalStep
                    };
                    yield break;
            }
        }

        run.FinalStatusReason = CreateFinalStatusReason(run, AgentRunStatus.Completed);
        CompleteQualityAssessment(run, AgentRunStatus.Completed);
        CompleteTelemetry(run);
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

    private static string CreateFinalStatusReason(AgentRun run, AgentRunStatus status)
    {
        return status switch
        {
            AgentRunStatus.Completed => "Completion evidence satisfied.",
            AgentRunStatus.BudgetExceeded => "Tool budget exhausted; checkpoint created.",
            AgentRunStatus.Failed when run.Verifications.Any(verification => !verification.IsSuccess) =>
                "At least one verification failed.",
            AgentRunStatus.Failed => string.IsNullOrWhiteSpace(run.CompletionReason)
                ? "Run failed final evidence checks."
                : run.CompletionReason,
            AgentRunStatus.Cancelled => "Run was cancelled.",
            _ => status.ToString()
        };
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
            AdaptiveRecoveryEnabled = context.AdaptiveRecoveryEnabled,
            AdaptiveAutoVerifyEnabled = context.AdaptiveAutoVerifyEnabled,
            VerificationCommands = context.VerificationCommands,
            InputArtifacts = context.InputArtifacts
        };
    }

    private static string BuildFinalContent(string assistantContent, AgentRun run, AgentRunStatus status)
    {
        var appendix = CreateFinalContentAppendix(run, status);
        return string.IsNullOrWhiteSpace(appendix)
            ? assistantContent
            : string.IsNullOrWhiteSpace(assistantContent)
                ? appendix
                : assistantContent.TrimEnd() + Environment.NewLine + Environment.NewLine + appendix;
    }

    private static string CreateFinalContentAppendix(AgentRun run, AgentRunStatus status)
    {
        if (status != AgentRunStatus.Completed)
        {
            return CreateIncompleteRunUserMessage(run);
        }

        var notes = new List<string>();
        if (string.Equals(run.CompletionEvidenceStatus, "risk", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("完成声明已降级：最终回复中的完成/验证声明缺少对应工具证据，请以本条证据说明为准。");
        }

        if (run.MutationToolSucceeded && run.Verifications.Count == 0)
        {
            notes.Add("完成证据：已记录文件修改；未运行验证。");
        }
        else if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            var passed = run.Verifications.Count(verification => verification.IsSuccess);
            notes.Add($"完成证据：验证未全部通过（{passed}/{run.Verifications.Count} 通过）。");
        }

        return string.Join(Environment.NewLine, notes);
    }

    private static string CreateIncompleteRunUserMessage(AgentRun run)
    {
        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            var failed = run.Verifications.First(verification => !verification.IsSuccess);
            return $"任务未完成：验证未通过（{failed.Command}，退出码 {failed.ExitCode}）。";
        }

        return string.IsNullOrWhiteSpace(run.CompletionReason)
            ? "任务未完成，需要继续处理。"
            : $"任务未完成：{run.CompletionReason}";
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

    private void CompleteFinalValidation(AgentRun run, string assistantContent)
    {
        var evidence = _completionEvidenceChecker.Check(assistantContent, run);
        run.CompletionEvidenceStatus = evidence.Status;
        run.CompletionEvidenceSummary = evidence.Summary;
        run.CanClaimModified = evidence.CanClaimModified;
        run.CanClaimVerified = evidence.CanClaimVerified;
        var checks = new List<string>
        {
            run.ToolBudgetExceeded ? "工具预算：已耗尽" : "工具预算：未耗尽",
            run.ToolApprovalRejectedCount > 0
                ? $"工具审批：{run.ToolApprovalRejectedCount} 次拒绝"
                : "工具审批：无拒绝",
            run.MutationToolSucceeded
                ? "项目修改：已记录修改工具"
                : "项目修改：未记录修改工具"
        };

        if (run.Verifications.Count > 0)
        {
            var successCount = run.Verifications.Count(verification => verification.IsSuccess);
            checks.Add($"验证：{successCount}/{run.Verifications.Count} 通过");
        }
        else
        {
            checks.Add("验证：未运行");
        }

        checks.Add(evidence.Summary);
        foreach (var risk in evidence.Risks)
        {
            checks.Add($"一致性风险：{risk}");
        }

        run.FinalValidationSummary = string.Join(Environment.NewLine, checks);
    }

    private void CompleteQualityAssessment(AgentRun run, AgentRunStatus status)
    {
        var originalStatus = run.Status;
        run.Status = status;
        var evaluation = _qualityEvaluator.Evaluate(run);
        run.QualityScore = evaluation.Score;
        run.QualitySummary = evaluation.Summary;
        run.StrategySuggestion = evaluation.StrategySuggestion;
        run.Status = originalStatus;
    }

    private static void CompleteTelemetry(AgentRun run)
    {
        run.Telemetry = AgentRunTelemetryBuilder.Build(run);
        run.OutcomeKind = AgentRunTelemetryBuilder.ClassifyOutcome(run);
    }

    private static void CompleteRecoverySuggestion(
        AgentRun run,
        AgentTaskExecutionPolicy policy,
        AgentRunStatus? targetStatus = null)
    {
        run.VerificationRecoveryPacket = AgentRunCheckpointSummaryBuilder.BuildVerificationRecoveryPacket(run);
        run.CheckpointSummary = AgentRunCheckpointSummaryBuilder.Build(run);
        run.CheckpointArtifactRefs = run.Artifacts
            .OrderByDescending(artifact => artifact.CreatedAt)
            .Take(8)
            .Select(artifact => string.IsNullOrWhiteSpace(artifact.ToolName)
                ? $"{artifact.Kind}:{artifact.Id}"
                : $"{artifact.ToolName}:{artifact.Kind}:{artifact.Id}")
            .ToList();

        if (run.ToolBudgetExceeded)
        {
            var recoveryStyle = policy.PreferCleanRetryRecovery
                ? "如果恢复包显示当前状态不可信，优先用重试入口从原始目标重新开始。"
                : "优先沿用 checkpoint 继续，避免重复已经完成的探索或计划项。";
            run.RecoverySuggestion =
                $"""
                继续完成这个已暂停的 Agent 任务。

                原始目标：
                {run.Goal}

                暂停原因：
                工具调用预算已耗尽。

                恢复包：
                {run.CheckpointSummary}

                继续要求：
                1. 先用 git_status 和必要的只读工具快速确认当前状态。
                2. 不要重复恢复包里已经完成的探索或计划项。
                3. 优先继续“未完成计划/下一步建议”里的事项。
                4. 如果需要修改，实际调用写入/编辑工具后再声称完成。
                5. 如果发生修改，优先运行项目验证命令或合适的测试。
                6. {recoveryStyle}
                """;
            return;
        }

        if (targetStatus == AgentRunStatus.Cancelled || run.Status == AgentRunStatus.Cancelled || run.Phase == "cancelled")
        {
            run.RecoverySuggestion =
                $"""
                继续这个已停止的 Agent 任务。

                原始目标：
                {run.Goal}

                停止原因：
                {(string.IsNullOrWhiteSpace(run.CompletionReason) ? "用户取消或请求中断。" : run.CompletionReason)}

                恢复包：
                {run.CheckpointSummary}

                继续要求：
                1. 先检查当前工作区状态，确认上一轮已经完成到哪里。
                2. 沿用恢复包中的上下文，不要重复已经完成的步骤。
                3. 从未完成的计划项或最后一个失败/运行中步骤继续。
                """;
            return;
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            run.RecoverySuggestion =
                $"""
                继续完成这个被工具审批中断的 Agent 任务。

                原始目标：
                {run.Goal}

                暂停原因：
                上一轮有工具被拒绝。

                恢复包：
                {run.CheckpointSummary}

                继续要求：
                1. 先说明接下来需要哪些工具、为什么需要。
                2. 如果用户没有重新授权，不要重复调用刚被拒绝的高风险工具。
                3. 优先用只读工具确认当前状态，再选择最小必要动作。
                4. {FormatApprovalRecoveryPreference(policy)}
                """;
            return;
        }

        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            run.RecoverySuggestion =
                $"""
                继续修复这个验证失败的 Agent 任务。

                原始目标：
                {run.Goal}

                暂停原因：
                上一轮验证未全部通过。

                恢复包：
                {run.CheckpointSummary}

                失败验证恢复包：
                {run.VerificationRecoveryPacket}

                继续要求：
                1. 优先查看失败验证恢复包中的命令、错误摘要和相关文件。
                2. 只修复导致验证失败的最小问题，不扩大范围。
                3. 修复后重新运行失败验证命令。
                """;
            return;
        }

        run.RecoverySuggestion =
            $"""
            复查并继续这个 Agent 任务。

            原始目标：
            {run.Goal}

            恢复包：
            {run.CheckpointSummary}

            继续要求：
            1. 先查看当前工作区状态和恢复包。
            2. 不要重复已完成步骤。
            3. 从下一步建议继续，或明确说明无需继续。
            """;
    }

    private static string CreateBudgetPausedUserMessage(AgentRun run)
    {
        var next = AgentRunCheckpointSummaryBuilder.GetNextStepSuggestion(run);
        return string.IsNullOrWhiteSpace(next)
            ? "工具预算已用完，任务已暂停。当前状态已经保存，可以继续追加预算完成剩余工作。"
            : $"工具预算已用完，任务已暂停。当前状态已经保存。\n\n下一步建议：{next}";
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

    private static string FormatApprovalRecoveryPreference(AgentTaskExecutionPolicy policy)
    {
        return policy.CautiousToolApproval
            ? "保持高风险工具说明优先，等用户确认后再执行写入或提交。"
            : "保持工具调用最小化。";
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

    // Groups the scheduled sub-agents into execution layers. A layer
    // is a set of decisions whose intra-batch dependencies are all
    // already complete; within a layer the harness dispatches every
    // sub-agent in parallel (Task.WhenAll), so the parent run no
    // longer pays N × per-sub-agent-latency for an N-agent plan.
    //
    // The dependency DAG lives on AgentSubAgentScheduleDecision.
    // DependsOn (planned sub-agent ids). The agent coordinator has
    // already filtered out decisions whose dependencies can never
    // be satisfied, so the set passed in is consistent: the only
    // thing that can go wrong is a cycle, which the safety counter
    // here guards against (we fall back to dumping the remaining
    // nodes into one layer rather than deadlock the run).
    //
    // Today the only template the coordinator actually schedules
    // is the read-only "explorer", and explorer plans don't carry
    // dependencies on each other — so this collapses to a single
    // layer in the common case. The general algorithm is here so
    // worker / verifier templates can be wired through the same
    // path later without re-architecting the harness.
    public static IReadOnlyList<IReadOnlyList<AgentSubAgentScheduleDecision>> ComputeSubAgentExecutionLayers(
        IReadOnlyList<AgentSubAgentScheduleDecision> scheduled)
    {
        if (scheduled.Count == 0)
        {
            return [];
        }

        var remaining = new HashSet<string>(
            scheduled.Select(decision => decision.PlannedSubAgentId),
            StringComparer.OrdinalIgnoreCase);
        var layers = new List<IReadOnlyList<AgentSubAgentScheduleDecision>>();
        var safety = scheduled.Count + 1;

        while (remaining.Count > 0 && safety-- > 0)
        {
            // A decision is ready when every dependency it still has
            // references is OUTSIDE the remaining set — i.e. either
            // already completed in an earlier layer, or never part
            // of this batch (skipped by the coordinator). Order is
            // preserved within a layer so event emission matches the
            // original single-runner timing.
            var ready = scheduled
                .Where(decision => remaining.Contains(decision.PlannedSubAgentId))
                .Where(decision => decision.DependsOn.All(dep => !remaining.Contains(dep)))
                .OrderBy(decision => decision.Order)
                .ToList();

            if (ready.Count == 0)
            {
                // Cycle or some other inconsistency we can't
                // reason about. Dump the rest into one layer so the
                // run makes progress instead of hanging the
                // agent loop forever. The dependency checker in
                // BuildPlannedSubAgentDecisions should prevent this
                // in practice, so this is belt + suspenders.
                ready = scheduled
                    .Where(decision => remaining.Contains(decision.PlannedSubAgentId))
                    .OrderBy(decision => decision.Order)
                    .ToList();
            }

            layers.Add(ready);
            foreach (var decision in ready)
            {
                remaining.Remove(decision.PlannedSubAgentId);
            }
        }

        return layers;
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
