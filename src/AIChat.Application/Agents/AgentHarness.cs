using System.Runtime.CompilerServices;
using System.Text.Json;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Thin harness around the model/tool loop. It owns run/step recording while the
// UI remains responsible for rendering events and collecting user approvals.
public sealed class AgentHarness
{
    private readonly AgentRunner _agentRunner;

    public AgentHarness(AgentRunner agentRunner)
    {
        _agentRunner = agentRunner;
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
            MaxToolRounds = request.Context.MaxToolRounds,
            RequiresProjectMutation = RequiresProjectMutation(request.Goal),
            StartedAt = DateTimeOffset.Now
        };
        request.Conversation.AgentRuns.Add(run);
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.RunStarted,
            Run = run
        };

        var stepNumber = 0;
        var contextStep = AddCompletedStep(
            run,
            ++stepNumber,
            AgentStepType.Model,
            "准备上下文",
            request.Goal,
            CreateContextStepOutput(run));
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.StepAdded,
            Run = run,
            Step = contextStep
        };

        var assistantContent = "";
        var stepByToolCallId = new Dictionary<string, AgentStep>(StringComparer.Ordinal);
        await foreach (var agentEvent in _agentRunner.RunAsync(
                           request.ChatRequest,
                           request.Settings,
                           request.Context,
                           cancellationToken))
        {
            switch (agentEvent.Type)
            {
                case AgentRunEventType.RawProviderEvent:
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.RawProviderEvent,
                        Run = run,
                        RawJson = agentEvent.RawJson
                    };
                    break;
                case AgentRunEventType.ContentDelta:
                    run.Phase = "responding";
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

                    run.Phase = ClassifyToolPhase(agentEvent.ToolCall.Name);
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
                case AgentRunEventType.BudgetExceeded:
                    run.ToolBudgetExceeded = true;
                    run.CompletionReason = "已达到工具调用轮数上限。";
                    yield return new AgentHarnessEvent
                    {
                        Type = AgentHarnessEventType.ContentDelta,
                        Run = run,
                        Content = agentEvent.Content
                    };
                    break;
                case AgentRunEventType.Completed:
                    CompleteMutationGuardrail(run);
                    CompleteFinalValidation(run);
                    CompleteRecoverySuggestion(run);
                    CompleteRun(run, AgentRunStatus.Completed);
                    var finalStep = AddCompletedStep(
                        run,
                        ++stepNumber,
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

        CompleteRun(run, AgentRunStatus.Completed);
    }

    private static string ClassifyToolPhase(string toolName)
    {
        return toolName switch
        {
            "list_files" or "read_file" or "search_text" or "git_status" or "git_diff" => "reading",
            "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit" => "editing",
            "run_build" or "run_test" => "verifying",
            _ => "working"
        };
    }

    private static bool RequiresProjectMutation(string goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return false;
        }

        var mutationWords = new[]
        {
            "创建", "新建", "生成", "实现", "写一个", "做一个", "加一个", "新增",
            "修改", "改成", "改为", "替换", "删除", "修复", "优化", "重构",
            "create", "implement", "write", "modify", "change", "replace", "fix", "update", "add"
        };
        return mutationWords.Any(word => goal.Contains(word, StringComparison.OrdinalIgnoreCase));
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

    private static void CompleteMutationGuardrail(AgentRun run)
    {
        if (run.RequiresProjectMutation &&
            !run.MutationToolSucceeded &&
            !run.ToolBudgetExceeded &&
            string.IsNullOrWhiteSpace(run.CompletionReason))
        {
            run.CompletionReason = "任务看起来需要修改项目，但本轮没有记录到成功的修改工具。";
        }
    }

    private static void CompleteFinalValidation(AgentRun run)
    {
        var checks = new List<string>
        {
            run.ToolBudgetExceeded ? "工具预算：已耗尽" : "工具预算：未耗尽",
            run.ToolApprovalRejectedCount > 0
                ? $"工具审批：{run.ToolApprovalRejectedCount} 次拒绝"
                : "工具审批：无拒绝",
            run.RequiresProjectMutation
                ? run.MutationToolSucceeded
                    ? "项目修改：已记录修改工具"
                    : "项目修改：未记录修改工具"
                : "项目修改：非修改类任务"
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

        run.FinalValidationSummary = string.Join(Environment.NewLine, checks);
    }

    private static void CompleteRecoverySuggestion(AgentRun run)
    {
        if (run.ToolBudgetExceeded)
        {
            run.RecoverySuggestion =
                $"继续完成：{run.Goal}\n请先读取上一轮最后的工具结果和当前工作区状态，缩小范围后继续。必要时把工具轮数预算提高到 {Math.Min(Math.Max(run.MaxToolRounds + 2, 2), 20)}。";
            return;
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            run.RecoverySuggestion =
                $"继续完成：{run.Goal}\n上一轮有工具被拒绝。请先说明需要哪些工具、为什么需要，再等待确认后继续。";
            return;
        }

        if (run.RequiresProjectMutation && !run.MutationToolSucceeded)
        {
            run.RecoverySuggestion =
                $"继续完成：{run.Goal}\n上一轮没有记录到成功的修改工具。请先检查相关文件，再实际调用写入或编辑工具完成修改。";
            return;
        }

        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            run.RecoverySuggestion =
                $"继续修复：{run.Goal}\n上一轮验证未全部通过。请优先查看失败验证输出，修复后重新运行验证。";
            return;
        }

        run.RecoverySuggestion =
            $"复查并继续：{run.Goal}\n请先查看上一轮运行摘要、工作区 diff 和验证结果，再决定是否需要继续修改。";
    }

    private static bool IsMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit";
    }

    private static string CreateContextStepOutput(AgentRun run)
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

        return string.Join(Environment.NewLine, lines);
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
            Input = input,
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
            Input = input,
            Output = output,
            StartedAt = now,
            CompletedAt = now
        };
        run.Steps.Add(step);
        return step;
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

        step.Output = output;
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
                DiffText = ExtractDiffForPath(preview.DiffText, changedFile.Path),
                OldChars = changedFile.OldChars,
                NewChars = changedFile.NewChars,
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
        run.Verifications.Add(new AgentVerification
        {
            RunId = run.Id,
            StepId = stepId,
            ToolCallId = toolCall.Id,
            ToolName = toolResult.ToolName,
            Command = parsed.Command,
            ExitCode = parsed.ExitCode,
            TimedOut = parsed.TimedOut,
            IsSuccess = !toolResult.IsError && parsed.ExitCode == 0 && !parsed.TimedOut,
            Output = parsed.Output,
            CreatedAt = DateTimeOffset.Now
        });
    }

    private static bool IsVerificationTool(string toolName)
    {
        return string.Equals(toolName, "run_build", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "run_test", StringComparison.OrdinalIgnoreCase);
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
            GetInt(element, "newChars", GetInt(element, "chars")));
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

    private static void CompleteRun(AgentRun run, AgentRunStatus status)
    {
        run.Complete(status);
    }

    private sealed record ChangedFileInfo(string Path, int OldChars, int NewChars);
    private sealed record VerificationInfo(string Command, int ExitCode, bool TimedOut, string Output);
}
