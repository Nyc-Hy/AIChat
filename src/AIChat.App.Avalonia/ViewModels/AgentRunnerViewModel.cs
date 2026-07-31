using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the inner agent loop: building the AgentHarness, streaming events,
// updating the activity feed, and persisting the conversation when the run
// finishes. Extracted from MainWindowViewModel in PR-13 so the host VM only
// owns the user-facing SendTaskCommand (validation + entry point) and
// cross-VM coordination.
//
// The host's IsRunning / StatusMessage / DraftPrompt / InputTokens stay the
// source of truth for the XAML bindings; the runner writes through the
// small set of Action/Func callbacks passed in. All other dependencies
// (activity feed, sidebar, conversation list, repository, chat service,
// tool registry, approval service) are held directly because the runner
// is the sole writer of those during a run.
public sealed partial class AgentRunnerViewModel : ObservableObject
{
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IApprovalService _approval;
    private readonly IAppRepository _repository;
    private readonly ActivityFeedViewModel _activityFeed;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ConversationListViewModel _conversationList;

    // Host-owned state setters. The host's IsRunning is what the XAML
    // binds to (and the CanExecute on SendTaskCommand depends on it), so
    // the runner writes through these callbacks instead of duplicating
    // the observable here.
    private readonly Action<bool> _setIsRunning;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<int> _setInputTokens;
    private readonly Action _clearDraftPrompt;
    private readonly Action<string> _setLastAssistantStatus;
    private readonly Action<AIChat.Domain.Chat.AgentPlan?> _updatePlan;
    private readonly Action<AIChat.Domain.Chat.AgentSubAgentRun> _upsertSubAgent;
    private readonly Action _clearSubAgentRuns;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<bool> _getNoWriteMode;

    public AgentRunnerViewModel(
        IChatCompletionService chatService,
        AgentToolRegistry toolRegistry,
        IApprovalService approval,
        IAppRepository repository,
        ActivityFeedViewModel activityFeed,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        Action<bool> setIsRunning,
        Action<string> setStatusMessage,
        Action<int> setInputTokens,
        Action clearDraftPrompt,
        Action<string> setLastAssistantStatus,
        Action<AIChat.Domain.Chat.AgentPlan?> updatePlan,
        Action<AIChat.Domain.Chat.AgentSubAgentRun> upsertSubAgent,
        Action clearSubAgentRuns,
        Func<AppSettings> getSettings,
        Func<bool> getNoWriteMode)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _approval = approval;
        _repository = repository;
        _activityFeed = activityFeed;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _setIsRunning = setIsRunning;
        _setStatusMessage = setStatusMessage;
        _setInputTokens = setInputTokens;
        _clearDraftPrompt = clearDraftPrompt;
        _setLastAssistantStatus = setLastAssistantStatus;
        _updatePlan = updatePlan;
        _upsertSubAgent = upsertSubAgent;
        _clearSubAgentRuns = clearSubAgentRuns;
        _getSettings = getSettings;
        _getNoWriteMode = getNoWriteMode;
    }

    // Entry point. The host has already validated the prompt, settings,
    // and project; this method assumes all preconditions hold.
    //
    // The host owns the CancellationTokenSource and exposes a StopTaskCommand
    // that cancels it. The token is forwarded to AgentHarness.RunAsync so
    // the inner loop halts at the next await point. OperationCanceledException
    // is caught here and surfaces as a "已停止" status on the assistant
    // bubble rather than a "失败" one.
    public async Task RunAsync(string prompt, AppSettings effectiveSettings, CancellationToken cancellationToken = default)
    {
        _setIsRunning(true);
        _clearDraftPrompt();
        _clearSubAgentRuns();
        var userItem = new ActivityItemViewModel("你", prompt, "已发送");
        var assistantItem = new ActivityItemViewModel(
            "AIChat",
            _getNoWriteMode() ? "正在以只读模式启动..." : "正在启动任务...",
            "运行中");
        _setLastAssistantStatus("运行中");
        _activityFeed.Add(userItem);
        _activityFeed.Add(assistantItem);
        _setStatusMessage("AIChat 正在读取上下文...");

        var project = _sidebar.CurrentProject!;
        var conversation = new Conversation
        {
            ProjectId = project.Id,
            Title = prompt.Length > 80 ? prompt[..80] : prompt,
            UpdatedAt = DateTimeOffset.Now
        };
        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = prompt,
            CreatedAt = DateTimeOffset.Now
        };
        var assistantMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.Assistant,
            Content = "",
            CreatedAt = DateTimeOffset.Now
        };
        conversation.Messages.Add(userMessage);
        conversation.Messages.Add(assistantMessage);

        try
        {
            var settings = _getSettings();
            var noWrite = _getNoWriteMode();
            var runtimeSettings = noWrite
                ? RuntimeSettingsBuilder.ReadOnly(settings, _toolRegistry)
                : RuntimeSettingsBuilder.Gui(settings, _toolRegistry);
            // AppSettings.UseTokenizerEstimation has been a real schema
            // field since PR-3 but the construction site always passed
            // a TokenizerContextEstimator — the flag was set, never
            // bound. Honor the setting now: false → simple chars-based
            // heuristic (faster, no SharpToken dependency, but rougher
            // numbers); true → tokenizer (default, billing-grade).
            // The default value on a fresh AppSettings is true so the
            // observable behaviour is unchanged unless the user
            // explicitly flips the flag.
            var contextEstimator = settings.UseTokenizerEstimation
                ? (IContextEstimator)new TokenizerContextEstimator()
                : new SimpleContextEstimator();
            var requestFactory = new AgentRequestFactory(
                new ConversationContextBuilder(
                    contextEstimator,
                    new SystemPromptBuilder()));
            var requestBuild = requestFactory.Build(new AgentRequestBuildRequest
            {
                Conversation = conversation,
                AssistantMessageId = assistantMessage.Id,
                EffectiveSettings = effectiveSettings,
                RuntimeSettings = runtimeSettings,
                ProjectName = project.Name,
                ProjectPath = project.Path,
                ProjectLoadSnapshot = BuildProjectSnapshot(project),
                PinnedContextItems = project.PinnedContext,
                InputArtifacts = project.InputArtifacts,
                MemoryEntries = project.Memories,
                ProjectToolPermissionModes = project.ProjectToolPermissionModes,
                VerificationCommands = project.VerificationCommands,
                RequestToolApprovalAsync = _approval.RequestApprovalAsync
            });

            // Push the authoritative input-tokens estimate to the host
            // so the status-bar context meter reflects what the agent
            // is actually about to send (the host's pre-build estimate
            // was based on a separate router call). The previous
            // SessionInsightsViewModel.BeginRun also touched a stack
            // of dead metrics (output, tool rounds, runtime, …) that
            // nothing read; the only consumer that survived was the
            // input-tokens cell the status bar read, which is now the
            // single source of truth here.
            _setInputTokens(ContextInputEstimator.Estimate(
                requestBuild.ContextPack?.EstimatedTokens ?? 0,
                prompt));

            var harness = new AgentHarness(
                new AgentRunner(_chatService, new AgentToolCatalog(_toolRegistry.All)));
            assistantItem.Detail = "";
            await foreach (var agentEvent in harness.RunAsync(new AgentHarnessRunRequest
            {
                Conversation = conversation,
                UserMessageId = userMessage.Id,
                AssistantMessageId = assistantMessage.Id,
                Goal = prompt,
                ChatRequest = requestBuild.ChatRequest,
                Settings = effectiveSettings,
                ContextPack = requestBuild.ContextPack,
                Context = requestBuild.AgentContext
            }, cancellationToken))
            {
                await ApplyAgentEventAsync(agentEvent, assistantItem, assistantMessage);
            }

            if (string.IsNullOrWhiteSpace(assistantItem.Detail))
            {
                assistantItem.Detail = "本次运行已结束，但没有可显示的文本。";
            }

            assistantItem.Status = "完成";
            _setLastAssistantStatus("完成");
            // Drop a "本次运行" summary bubble into the activity feed
            // so the user can see at a glance what happened — file
            // count, tool call count, duration — without opening the
            // git modal or scrolling through tool cards.
            var run = conversation.AgentRuns.LastOrDefault();
            if (run is not null)
            {
                // Pass isReadOnly so a no-write run with 0 changes
                // can be tagged in the summary — the user sent a
                // refactor / fix / add request, the agent did all
                // the planning, nothing landed, and the "改 0 个
                // 文件" line by itself doesn't tell them whether
                // the agent's plan was a no-op or whether read-only
                // mode silently swallowed every write. Tagging the
                // line in the summary keeps the cause visible
                // without an extra system bubble.
                _activityFeed.Add("本次运行", BuildRunSummary(run, isReadOnly: noWrite), "完成");
            }
            conversation.UpdatedAt = DateTimeOffset.Now;
            project.Conversations.Add(conversation);
            project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            _conversationList.Refresh(project, conversation.Id);
            _setStatusMessage("完成。");
        }
        catch (OperationCanceledException)
        {
            assistantItem.Status = "已停止";
            _setLastAssistantStatus("已停止");
            if (string.IsNullOrEmpty(assistantItem.Detail))
            {
                assistantItem.Detail = "本次运行已停止。";
            }
            // Re-throw so the host's SendTaskCommand can set its own
            // status message; the host owns the user-facing status bar.
            throw;
        }
        catch (Exception ex)
        {
            assistantItem.Status = "失败";
            _setLastAssistantStatus("失败");
            assistantItem.Detail = $"请求失败：{ex.Message}";
            _setStatusMessage("请求失败。");
            // Re-throw so the host's SendTaskCommand catch can drop a
            // toast — the runner never knows about the toast service.
            throw;
        }
        finally
        {
            _setIsRunning(false);
        }
    }

    private async Task ApplyAgentEventAsync(
        AgentHarnessEvent agentEvent,
        ActivityItemViewModel assistantItem,
        ChatMessage assistantMessage)
    {
        switch (agentEvent.Type)
        {
            case AgentHarnessEventType.StepAdded:
                // The harness updates Run.Plan whenever the agent adds
                // a step. Forward the latest plan to the host so the
                // plan panel stays in sync.
                _updatePlan(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.SubAgentStarted:
            case AgentHarnessEventType.SubAgentCompleted:
                // Sub-agent runs are surfaced as a sub-section of the
                // plan panel (template + task + status + duration).
                // Upsert so the started event creates the row and the
                // completed event updates the same row in place.
                if (agentEvent.SubAgentRun is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => _upsertSubAgent(agentEvent.SubAgentRun));
                }
                _updatePlan(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.PhaseChanged:
                if (!string.IsNullOrWhiteSpace(agentEvent.PhaseTransition?.Summary))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _setStatusMessage(agentEvent.PhaseTransition.Summary);
                    });
                }

                break;
            case AgentHarnessEventType.ToolCall:
                if (!string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Name))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _activityFeed.Add(
                            "正在读取",
                            FriendlyToolSummary(agentEvent.ToolCall.Name),
                            "工具");
                    });
                }

                break;
            case AgentHarnessEventType.ToolApprovalRejected:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _activityFeed.Add(
                        "已跳过操作",
                        agentEvent.ToolPreview?.Summary ?? "此操作需要确认后才能执行。",
                        "已阻止");
                });
                break;
            case AgentHarnessEventType.ToolResult:
                if (agentEvent.ToolResult?.IsError == true)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _activityFeed.Add(
                            "工具问题",
                            agentEvent.ToolResult.Content,
                            "需查看");
                    });
                }

                // update_plan mutates Run.Plan directly and emits a
                // ToolResult rather than a StepAdded, so the plan
                // panel won't see the new items unless we forward
                // here too. Cheap to do on every ToolResult — the
                // host just clears + re-adds the same items.
                _updatePlan(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.ContentDelta:
                if (!string.IsNullOrEmpty(agentEvent.Content))
                {
                    assistantMessage.Content += agentEvent.Content;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // First content delta after the "正在启动
                        // 任务..." placeholder: clear the placeholder
                        // (replace, don't append) so the rendered
                        // markdown shows the model's actual response
                        // rather than "正在启动任务...Hello there...".
                        // Subsequent deltas append as usual. The flag
                        // lives on the bubble itself so the lambda
                        // doesn't have to carry per-run state.
                        if (!assistantItem.HasReceivedFirstContent)
                        {
                            assistantItem.Detail = agentEvent.Content;
                            assistantItem.HasReceivedFirstContent = true;
                        }
                        else
                        {
                            assistantItem.Detail += agentEvent.Content;
                        }
                        _setStatusMessage("正在接收回复...");
                    });
                }

                break;
            case AgentHarnessEventType.RunCompleted:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _setStatusMessage(agentEvent.Run?.CompletionReason is { Length: > 0 } reason ? reason : "运行完成。");
                });
                break;
        }
    }

    private static string FriendlyToolSummary(string toolName)
    {
        return toolName switch
        {
            "list_files" => "正在列出项目文件",
            "read_file" => "正在读取文件",
            "search_text" => "正在搜索项目",
            "read_input_artifact" => "正在读取输入资料",
            "update_plan" => "正在更新任务计划",
            _ => $"正在使用 {toolName}"
        };
    }

    // Build the "本次运行" summary the host drops into the activity
    // feed right after a run lands. Keeps to one line of plain text
    // so the system bubble stays scannable: files / tools / duration.
    // Explorer / worker sub-agent counts + verification results are
    // surfaced when the run actually used them — silent otherwise so
    // a simple chat exchange doesn't look heavier than it was.
    //
    // The caller passes isReadOnly so a no-write run that touched
    // zero files can carry a "只读" tag — the user sent a refactor
    // prompt, the agent did all the planning, nothing landed, and
    // "改 0 个文件" by itself doesn't tell them whether to flip
    // read-only off and retry or whether the agent decided the
    // task was already done. The tag makes the cause visible
    // without an extra system bubble.
    public static string BuildRunSummary(AIChat.Domain.Chat.AgentRun run, bool isReadOnly = false)
    {
        var fileChangeCount = run.FileChanges?.Count ?? 0;
        var toolCount = run.ToolCallCount;
        var duration = run.CompletedAt.HasValue
            ? FormatDuration(run.CompletedAt.Value - run.StartedAt)
            : "未知时长";
        var subAgentCount = run.SubAgentRuns?.Count ?? 0;
        var verificationCount = run.Verifications?.Count ?? 0;
        var verificationPassed = run.Verifications?.Count(verification => verification.IsSuccess) ?? 0;

        var parts = new List<string>();
        if (fileChangeCount > 0)
        {
            parts.Add($"改 {fileChangeCount} 个文件");
        }
        if (toolCount > 0)
        {
            parts.Add($"用 {toolCount} 次工具");
        }
        if (subAgentCount > 0)
        {
            parts.Add($"派 {subAgentCount} 个子 Agent");
        }
        if (verificationCount > 0)
        {
            parts.Add($"验证 {verificationPassed}/{verificationCount} 通过");
        }
        if (isReadOnly && fileChangeCount == 0 && toolCount > 0)
        {
            parts.Add("只读模式");
        }
        parts.Add(duration);

        return string.Join(" · ", parts);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 1)
        {
            return "<1s";
        }
        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }
        return $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }

    private async Task SaveProjectsAsync()
    {
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var index = projects.FindIndex(project => project.Id == _sidebar.CurrentProject?.Id);
        if (index >= 0)
        {
            projects[index] = _sidebar.CurrentProject!;
        }
        else if (_sidebar.CurrentProject is not null)
        {
            projects.Add(_sidebar.CurrentProject);
        }

        await _repository.SaveProjectsAsync(projects);
    }

    private static string BuildProjectSnapshot(ProjectWorkspace project)
    {
        var snapshot = ProjectLoadSnapshotBuilder.Build(project);
        return string.Join(Environment.NewLine, [
            snapshot.HealthText,
            snapshot.ProfileText,
            snapshot.ActivityText,
            snapshot.RecommendationText
        ]);
    }
}
