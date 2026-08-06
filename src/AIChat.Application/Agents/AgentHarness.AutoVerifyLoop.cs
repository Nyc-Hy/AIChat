using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Prompting;
using AIChat.Application.Security;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Post-mutation auto-verify loop — split out from the main
// AgentHarness partial so the orchestration file stays focused
// on the run loop. RunAutoVerifyLoopAsync is invoked by
// RunToolLoopPhaseAsync when the runner emits Completed and
// the run mutated files (MutationToolSucceeded). For each
// verification command, it previews / executes the
// corresponding IAgentTool, records a verification result, and
// on failure builds a synthetic "verification failed, please
// fix" message that gets fed back to the main model for
// another fix round. Verification always runs once; MaxAutoFixRounds
// limits only the number of repair attempts after that first pass.
// The loop terminates via yield break on Cancelled /
// BudgetExceeded / Error. Pulled out of the main file because
// it was 220 lines of dense switch / event-stream handling that
// distracted from reading the run loop above it.
public sealed partial class AgentHarness
{
    private async IAsyncEnumerable<AgentHarnessEvent> RunAutoVerifyLoopAsync(
        AgentRun run,
        AgentHarnessRunRequest request,
        Dictionary<string, AgentStep> stepByToolCallId,
        AppSettings settings,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxFixRounds = Math.Max(0, context.MaxAutoFixRounds);
        for (var verificationAttempt = 0; verificationAttempt <= maxFixRounds; verificationAttempt++)
        {
            var allPassed = true;
            var failureMessages = new List<string>();

            foreach (var cmd in context.VerificationCommands)
            {
                var toolName = GetVerificationToolName(cmd.Command);
                var tool = CreateVerificationTool(toolName);
                if (tool is null)
                {
                    var blockedMessage = "该验证命令不在安全允许列表中，未执行。";
                    var blockedCallId = $"auto-verify-{run.Id}-{verificationAttempt}-{cmd.Id}";
                    var blockedStep = AddRunningStep(
                        run,
                        run.Steps.Count + 1,
                        AgentStepType.ToolCall,
                        $"自动验证：{cmd.Name}",
                        cmd.Command,
                        blockedCallId,
                        "verification_blocked");
                    blockedStep.Output = blockedMessage;
                    blockedStep.IsError = true;
                    blockedStep.Status = AgentStepStatus.Failed;
                    blockedStep.CompletedAt = DateTimeOffset.Now;
                    stepByToolCallId[blockedCallId] = blockedStep;
                    run.Verifications.Add(new AgentVerification
                    {
                        RunId = run.Id,
                        StepId = blockedStep.Id,
                        ToolCallId = blockedCallId,
                        ToolName = "verification_blocked",
                        Command = cmd.Command,
                        ExitCode = 1,
                        IsSuccess = false,
                        Output = blockedMessage,
                        Summary = "验证命令已阻止",
                        CreatedAt = DateTimeOffset.Now
                    });
                    allPassed = false;
                    failureMessages.Add($"[{cmd.Name}] {blockedMessage}");
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ToolResult,
                        Run = run,
                        Step = blockedStep,
                        ToolCall = new ChatToolCall
                        {
                            Id = blockedCallId,
                            Name = "verification_blocked",
                            ArgumentsJson = cmd.Command
                        },
                        ToolResult = new AgentToolResult
                        {
                            ToolName = "verification_blocked",
                            IsError = true,
                            Content = blockedMessage
                        }
                    };
                    continue;
                }

                var toolCallId = $"auto-verify-{run.Id}-{verificationAttempt}-{cmd.Id}";
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

            if (verificationAttempt >= maxFixRounds)
            {
                run.CompletionReason = maxFixRounds == 0
                    ? "自动验证失败，未配置自动修复轮次。"
                    : $"自动验证在 {maxFixRounds} 轮修复后仍未通过。";
                yield return new AgentHarnessEvent
                {
                    Type = AgentHarnessEventType.ContentDelta,
                    Run = run,
                    Content = run.CompletionReason
                };
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
                    case AgentRunEventType.RunUsage:
                        // 2026-08-05: forward the auto-verify
                        // round's per-call usage. Same
                        // intent as the main run's RunUsage
                        // case — the runner reads it to
                        // surface the cache hit rate.
                        if (fixEvent.Usage is not null)
                        {
                            yield return new AgentHarnessEvent
                            {
                                Type = AgentHarnessEventType.RunUsage,
                                Run = run,
                                Usage = fixEvent.Usage
                            };
                        }
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
                        run.Status = AgentRunStatus.BudgetExceeded;
                        run.CompletionReason = "自动修复阶段已达到工具调用轮数上限。";
                        yield break;
                    case AgentRunEventType.Cancelled:
                        run.Status = AgentRunStatus.Cancelled;
                        run.CompletionReason = string.IsNullOrWhiteSpace(fixEvent.Content)
                            ? "自动修复阶段已取消。"
                            : SensitiveDataRedactor.RedactText(fixEvent.Content);
                        yield break;
                    case AgentRunEventType.Error:
                        run.Status = AgentRunStatus.Failed;
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
}
