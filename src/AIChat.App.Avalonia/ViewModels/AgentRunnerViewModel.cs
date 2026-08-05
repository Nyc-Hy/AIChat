using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Context;
using AIChat.Application.Llm.Resilience;
using AIChat.Application.Prompting;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Wraps the agent loop (AgentHarness) for the host. The host
// (via AgentHostViewModel) owns all run-state — IsRunning,
// StatusMessage, InputTokens, LastAssistantStatus, DraftPrompt,
// PlanItems, SubAgentRuns — and the runner writes to those
// properties directly instead of through 10+ Action/Func
// callbacks. The few fields that live on the host instead of
// AgentHost (StatusMessage, AppSettings, NoWriteMode) are read
// through the host's public properties.
//
// The runner's only other collaborators (activity feed, sidebar,
// conversation list, repository, chat service, tool registry,
// approval service) are held directly because the runner is the
// sole writer of those during a run.
public sealed partial class AgentRunnerViewModel : ObservableObject
{
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IApprovalService _approval;
    private readonly IAppRepository _repository;
    private readonly ActivityFeedViewModel _activityFeed;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ConversationListViewModel _conversationList;
    private readonly AgentHostViewModel _host;

    public AgentRunnerViewModel(
        IChatCompletionService chatService,
        AgentToolRegistry toolRegistry,
        IApprovalService approval,
        IAppRepository repository,
        ActivityFeedViewModel activityFeed,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        AgentHostViewModel host)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _approval = approval;
        _repository = repository;
        _activityFeed = activityFeed;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _host = host;
    }

    // Entry point. The host has already validated the prompt,
    // settings, and project; this method assumes all preconditions
    // hold.
    //
    // The host owns the CancellationTokenSource and exposes a
    // StopTaskCommand that cancels it. The token is forwarded to
    // AgentHarness.RunAsync so the inner loop halts at the next
    // await point. OperationCanceledException is caught here and
    // surfaces as a "已停止" status on the assistant bubble rather
    // than a "失败" one.
    public async Task RunAsync(
        string prompt,
        AppSettings effectiveSettings,
        string continuedFromRunId = "",
        string retriedFromRunId = "",
        CancellationToken cancellationToken = default)
    {
        _host.IsRunning = true;
        _host.DraftPrompt = "";
        _host.ClearSubAgentRuns();
        var userItem = new ActivityItemViewModel("你", prompt, "已发送");
        var assistantItem = new ActivityItemViewModel(
            "AIChat",
            _host.GetNoWriteMode() ? "正在以只读模式启动..." : "正在启动任务...",
            "运行中");
        _host.LastAssistantStatus = "运行中";
        _activityFeed.Add(userItem);
        _activityFeed.Add(assistantItem);
        _host.SetStatusMessage("AIChat 正在读取上下文...");

        var project = _sidebar.CurrentProject!;
        var parentRunId = string.IsNullOrWhiteSpace(continuedFromRunId)
            ? retriedFromRunId
            : continuedFromRunId;
        // Wave 2: conversations 改成 sessions,sidebar 在 ApplyProject 时已
        // 加载 _sidebar.CurrentProjectSessions(绑到当前 project 的 Project 类 sessions)。
        var conversation = string.IsNullOrWhiteSpace(parentRunId)
            ? null
            : _sidebar.CurrentProjectSessions.FirstOrDefault(item =>
                item.AgentRuns.Any(run => run.Id == parentRunId));
        var isExistingConversation = conversation is not null;
        conversation ??= new Project
        {
            WorkspaceId = project.Id,
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
            // 2026-08-05: think-block parser for the
            // AI bubble. Maintained across the
            // stream lifetime so partial ``
            // tags at chunk boundaries don't leak
            // into the visible content. The
            // parser is created fresh per run
            // (the user's expectation is that
            // each new turn starts a new chain,
            // not that it concatenates onto the
            // previous one).
            var thinkParser = new ThinkBlockParser();
            var settings = _host.GetSettings();
            var noWrite = _host.GetNoWriteMode();
            var runtimeSettings = noWrite
                ? RuntimeSettingsBuilder.ReadOnly(settings, _toolRegistry)
                : RuntimeSettingsBuilder.Gui(settings, _toolRegistry);
            // AppSettings.UseTokenizerEstimation has been a real
            // schema field since PR-3 but the construction site
            // always passed a TokenizerContextEstimator — the flag
            // was set, never bound. Honor the setting now: false →
            // simple chars-based heuristic (faster, no SharpToken
            // dependency, but rougher numbers); true → tokenizer
            // (default, billing-grade). The default value on a
            // fresh AppSettings is true so the observable behaviour
            // is unchanged unless the user explicitly flips the
            // flag.
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
                ProjectPath = project.TryGetPrimaryPath() ?? "",
                ProjectLoadSnapshot = BuildProjectSnapshot(project, _sidebar.CurrentProjectSessions),
                PinnedContextItems = project.PinnedContext,
                InputArtifacts = project.InputArtifacts,
                MemoryEntries = project.Memories,
                ProjectToolPermissionModes = project.ProjectToolPermissionModes,
                VerificationCommands = project.VerificationCommands,
                RequestToolApprovalAsync = _approval.RequestApprovalAsync
            });

            // Push the authoritative input-tokens estimate to the
            // host so the status-bar context meter reflects what
            // the agent is actually about to send (the host's
            // pre-build estimate was based on a separate router
            // call).
            _host.InputTokens = ContextInputEstimator.Estimate(
                requestBuild.ContextPack?.EstimatedTokens ?? 0,
                prompt);

            // AppSettings.RetryMaxAttempts is a real schema field
            // (clamped on every load by the inline normalize in
            // MainWindowViewModel ctor, persisted through
            // ProtectedSettingsSerializer), but the construction
            // site at AgentRunnerViewModel always used the default
            // 'new RetryPolicy()' which has 3 hard-coded — the
            // field was a schema property with no observable
            // effect. Wire it through now: a user who bumps
            // retries to 5 in their settings file actually gets 5
            // retries. The default value on a fresh AppSettings
            // is 3 so observable behaviour is unchanged unless the
            // user explicitly edits the value.
            // AgentToolRegistry is now the single surface for
            // tool resolution + execution (the plan-3.8 merge
            // dropped AgentToolCatalog and ToolExecutionService);
            // pass it directly to AgentRunner.
            var harness = new AgentHarness(
                new AgentRunner(
                    _chatService,
                    _toolRegistry,
                    retryPolicy: new RetryPolicy(maxRetries: settings.RetryMaxAttempts)));
            assistantItem.Detail = "";
            // 2026-08-05: tool-call records keyed by
            // call id (or name when id is missing on
            // the started chunk — the harness can
            // emit the name first and the id a few
            // chunks later on streaming tool calls).
            // The ToolResult handler updates the
            // matching record's status + duration
            // rather than emitting a new system
            // bubble per call. This consolidates
            // the 10–30+ per-call "正在读取" /
            // "工具问题" rows that used to push the
            // real conversation off-screen on long
            // agent runs.
            var toolRecords = new Dictionary<string, ToolCallRecord>(StringComparer.OrdinalIgnoreCase);
            await foreach (var agentEvent in harness.RunAsync(new AgentHarnessRunRequest
            {
                Conversation = conversation,
                UserMessageId = userMessage.Id,
                AssistantMessageId = assistantMessage.Id,
                Goal = prompt,
                ChatRequest = requestBuild.ChatRequest,
                Settings = effectiveSettings,
                ContextPack = requestBuild.ContextPack,
                Context = requestBuild.AgentContext,
                ContinuedFromRunId = continuedFromRunId,
                RetriedFromRunId = retriedFromRunId
            }, cancellationToken))
            {
                await ApplyAgentEventAsync(agentEvent, assistantItem, assistantMessage, thinkParser, toolRecords);
            }
            // 2026-08-05: flush any partial ``
            // tag the parser was holding when the
            // stream ended (a clean [DONE] path
            // doesn't need this — the parser
            // already drained the buffer — but a
            // truncated stream that ends mid-tag
            // would otherwise drop the literal
            // tag text). Force-emit the pending
            // buffer to the visible content so
            // the user sees the raw tag rather
            // than a silent loss.
            thinkParser.Flush();
            if (!string.IsNullOrEmpty(thinkParser.VisibleContent))
            {
                assistantMessage.Content += thinkParser.VisibleContent;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    assistantItem.Detail = thinkParser.VisibleContent;
                });
            }

            var run = conversation.AgentRuns.LastOrDefault();
            var terminal = GetRunTerminalPresentation(run);
            if (string.IsNullOrWhiteSpace(assistantItem.Detail))
            {
                assistantItem.Detail = terminal.FallbackDetail;
            }

            assistantItem.Status = terminal.ActivityStatus;
            _host.LastAssistantStatus = terminal.ActivityStatus;
            // 2026-08-05: pin the run summary to
            // the AI bubble as a footer instead
            // of dropping a separate "本次运行"
            // system bubble. The previous shape
            // (user → AI → system → user → AI →
            // system ...) made long agent runs
            // feel like the conversation was
            // half-bubbles, half-status lines.
            // Now the summary lives on the AI
            // bubble itself so the activity feed
            // reads as a strict user / AI rhythm
            // and the stats are anchored to the
            // response that produced them.
            //
            // isReadOnly is passed so a no-write
            // run with 0 changes can be tagged
            // in the summary — the user sent a
            // refactor / fix / add request, the
            // agent did all the planning,
            // nothing landed, and the "改 0 个
            // 文件" line by itself doesn't tell
            // them whether the agent's plan was
            // a no-op or whether read-only
            // mode silently swallowed every
            // write. The tag keeps the cause
            // visible.
            if (run is not null)
            {
                _host.RecordLastRun(run);
                assistantItem.RunSummary = BuildRunSummary(
                    run,
                    isReadOnly: noWrite,
                    usage: _host.LastRunUsage);
            }
            await PersistConversationAsync(project, conversation, isExistingConversation);
            _host.SetStatusMessage(terminal.StatusMessage);
        }
        catch (OperationCanceledException)
        {
            var run = conversation.AgentRuns.LastOrDefault();
            if (run is { Status: AgentRunStatus.Running })
            {
                run.Complete(AgentRunStatus.Cancelled, completionReason: "运行已由用户停止。");
            }
            if (run is not null)
            {
                _host.RecordLastRun(run);
            }
            assistantItem.Status = "已停止";
            _host.LastAssistantStatus = "已停止";
            if (string.IsNullOrEmpty(assistantItem.Detail))
            {
                assistantItem.Detail = "本次运行已停止。";
            }
            await PersistConversationSafelyAsync(project, conversation, isExistingConversation);
            // Re-throw so the host's SendTaskCommand can set
            // its own status message; the host owns the
            // user-facing status bar.
            throw;
        }
        catch (Exception ex)
        {
            var run = conversation.AgentRuns.LastOrDefault();
            if (run is { Status: AgentRunStatus.Running })
            {
                run.Complete(AgentRunStatus.Failed, completionReason: ex.Message);
            }
            if (run is not null)
            {
                _host.RecordLastRun(run);
            }
            assistantItem.Status = "失败";
            _host.LastAssistantStatus = "失败";
            assistantItem.Detail = $"请求失败：{ex.Message}";
            _host.SetStatusMessage("请求失败。");
            await PersistConversationSafelyAsync(project, conversation, isExistingConversation);
            // Re-throw so the host's SendTaskCommand catch can
            // drop a toast — the runner never knows about the
            // toast service.
            throw;
        }
        finally
        {
            _host.IsRunning = false;
        }
    }

    public static RunTerminalPresentation GetRunTerminalPresentation(AgentRun? run)
    {
        var reason = run?.CompletionReason?.Trim() ?? "";
        return run?.Status switch
        {
            AgentRunStatus.Completed => new("完成", string.IsNullOrEmpty(reason) ? "完成。" : reason,
                "本次运行已结束，但没有可显示的文本。"),
            AgentRunStatus.BudgetExceeded => new("预算暂停", string.IsNullOrEmpty(reason) ? "工具预算已用完，任务已暂停。" : reason,
                "工具预算已用完，任务已暂停。你可以继续上一次任务。"),
            AgentRunStatus.Cancelled => new("已停止", string.IsNullOrEmpty(reason) ? "已停止。" : reason,
                "本次运行已停止。"),
            AgentRunStatus.Failed => new("失败", string.IsNullOrEmpty(reason) ? "请求失败。" : reason,
                "本次运行失败。你可以继续上一次任务。"),
            _ => new("失败", "运行记录缺少有效终态。", "本次运行没有产生有效的结束状态。")
        };
    }

    private async Task ApplyAgentEventAsync(
        AgentHarnessEvent agentEvent,
        ActivityItemViewModel assistantItem,
        ChatMessage assistantMessage,
        // 2026-08-05: parser + tool records passed
        // through to keep the per-run state
        // across streaming events. The parser
        // is local to the run lifetime so a
        // previous run's think chain doesn't
        // bleed into the next turn. The dict
        // keys tool-call-id → ToolCallRecord
        // so the ToolResult handler can find
        // and update the matching row in place
        // (replaces the previous "one new
        // system bubble per event" pattern).
        ThinkBlockParser thinkParser,
        Dictionary<string, ToolCallRecord> toolRecords)
    {
        switch (agentEvent.Type)
        {
            case AgentHarnessEventType.StepAdded:
                // The harness updates Run.Plan whenever the agent
                // adds a step. Forward the latest plan to the host
                // so the plan panel stays in sync. The harness
                // yields events on whatever thread the LLM stream
                // resumes on, so every host-state mutation
                // (including the plan list and the sub-agent
                // rows) is marshalled to the UI thread first —
                // mutating an ItemsControl-bound
                // ObservableCollection from a thread-pool thread
                // throws or corrupts the render.
                await UpdatePlanOnUiThreadAsync(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.SubAgentStarted:
            case AgentHarnessEventType.SubAgentCompleted:
                // Sub-agent runs are surfaced as a sub-section of
                // the plan panel (template + task + status +
                // duration). Upsert so the started event creates
                // the row and the completed event updates the
                // same row in place. Both the upsert and the plan
                // refresh run on the UI thread — the harness
                // event may have arrived on a worker thread.
                if (agentEvent.SubAgentRun is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => _host.UpsertSubAgentRun(agentEvent.SubAgentRun));
                }
                await UpdatePlanOnUiThreadAsync(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.PhaseChanged:
                if (!string.IsNullOrWhiteSpace(agentEvent.PhaseTransition?.Summary))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _host.SetStatusMessage(agentEvent.PhaseTransition.Summary);
                    });
                }

                break;
            case AgentHarnessEventType.ToolCall:
                // 2026-08-05: append a ToolCallRecord to
                // the AI bubble's tool-calls list
                // instead of emitting a "正在读取"
                // system bubble. The new
                // collapsible "工具调用 (N)"
                // section on the AI bubble is the
                // single place the user sees the
                // tool chain — long agent runs no
                // longer push the real
                // conversation off-screen with
                // 10–30+ system rows.
                if (!string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Name))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var record = new ToolCallRecord
                        {
                            Name = agentEvent.ToolCall.Name,
                            Summary = FriendlyToolSummary(agentEvent.ToolCall.Name),
                            StartedAt = DateTimeOffset.Now,
                            Status = "运行中"
                        };
                        var key = !string.IsNullOrWhiteSpace(agentEvent.ToolCall.Id)
                            ? agentEvent.ToolCall.Id
                            : $"__pending::{agentEvent.ToolCall.Name}::{assistantItem.ToolCalls.Count}";
                        toolRecords[key] = record;
                        assistantItem.ToolCalls.Add(record);
                    });
                }

                break;
            case AgentHarnessEventType.ToolApprovalRejected:
                // 2026-08-05: tool rejections are still
                // surfaced inline. The XAML's
                // approval-modal has its own
                // affordance, and the system
                // bubble is the only signal that
                // the runner-level state machine
                // sees — it's the rare case where
                // a single line is genuinely the
                // best fit. Kept for now; if the
                // tool-call consolidation grows to
                // cover this too the rejection
                // becomes a ToolCallRecord with a
                // "已阻止" status.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _activityFeed.Add(
                        "已跳过操作",
                        agentEvent.ToolPreview?.Summary ?? "此操作需要确认后才能执行。",
                        "已阻止");
                });
                break;
            case AgentHarnessEventType.ToolResult:
                // 2026-08-05: update the matching
                // ToolCallRecord in place rather
                // than emitting a new system
                // bubble. The record is keyed by
                // tool-call-id (or the pending
                // name+index key the started-event
                // handler used when the id wasn't
                // available yet — the harness can
                // emit the name before the id on
                // streaming tool calls). Falls
                // through to the old "工具问题"
                // system-bubble path when the
                // started event was missed (e.g.
                // a tool that was approved at
                // session level and fired without
                // a corresponding ToolCall event
                // — defensive, shouldn't happen in
                // practice).
                var resultKey = !string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Id)
                    ? agentEvent.ToolCall!.Id
                    : $"__pending::{agentEvent.ToolCall?.Name}::{assistantItem.ToolCalls.Count - 1}";
                if (agentEvent.ToolResult is not null
                    && !string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Id)
                    && toolRecords.TryGetValue(agentEvent.ToolCall.Id, out var existing))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        existing.CompletedAt = DateTimeOffset.Now;
                        existing.IsError = agentEvent.ToolResult.IsError;
                        existing.ErrorMessage = agentEvent.ToolResult.IsError
                            ? (agentEvent.ToolResult.Content ?? "").Split('\n').FirstOrDefault() ?? ""
                            : "";
                        existing.Status = agentEvent.ToolResult.IsError ? "失败" : "完成";
                    });
                }
                else if (agentEvent.ToolResult?.IsError == true)
                {
                    // Defensive fallback — the
                    // started event was missed or
                    // the id changed mid-stream.
                    // Keep the old behavior so the
                    // error still surfaces.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _activityFeed.Add(
                            "工具问题",
                            agentEvent.ToolResult.Content,
                            "需查看");
                    });
                }

                // update_plan mutates Run.Plan directly and emits
                // a ToolResult rather than a StepAdded, so the
                // plan panel won't see the new items unless we
                // forward here too. Cheap to do on every
                // ToolResult — the host just clears + re-adds the
                // same items. Same UI-thread dispatch as
                // StepAdded above.
                await UpdatePlanOnUiThreadAsync(agentEvent.Run?.Plan);
                break;
            case AgentHarnessEventType.ContentDelta:
                if (!string.IsNullOrEmpty(agentEvent.Content))
                {
                    // 2026-08-05: feed the think-block
                    // parser. The parser splits the
                    // delta into the visible content
                    // (the answer) and the hidden
                    // `` chain. Storing the
                    // visible content in
                    // assistantMessage.Content (not
                    // the raw delta with the
                    // `` tags inline) keeps the
                    // conversation log clean — a
                    // future "export as markdown"
                    // feature gets the answer
                    // without the chain noise.
                    thinkParser.Append(agentEvent.Content);
                    var visibleChunk = thinkParser.VisibleContent;
                    // The parser is stateful; reset
                    // VisibleContent so the next
                    // Append() reports the diff,
                    // not the cumulative total.
                    thinkParser.ResetVisibleDelta();
                    assistantMessage.Content += visibleChunk;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // First content delta after
                        // the "正在启动任务..."
                        // placeholder: clear the
                        // placeholder (replace,
                        // don't append) so the bubble
                        // doesn't render as
                        // "正在启动任务...Hello there..."
                        // in the markdown view. The
                        // flag lives on the bubble
                        // itself so the lambda
                        // doesn't have to carry
                        // per-run state.
                        if (!assistantItem.HasReceivedFirstContent)
                        {
                            assistantItem.Detail = visibleChunk;
                            assistantItem.HasReceivedFirstContent = true;
                        }
                        else
                        {
                            assistantItem.Detail += visibleChunk;
                        }
                        // The think chain is
                        // appended separately. The
                        // XAML's collapsible "💭
                        // 思考过程" section
                        // (IsVisible via HasThinking)
                        // opens automatically when
                        // the chain starts
                        // accumulating and the
                        // user can dismiss it once
                        // the answer is done
                        // streaming.
                        assistantItem.Thinking = thinkParser.Thinking;
                        _host.SetStatusMessage("正在接收回复...");
                    });
                }

                break;
            case AgentHarnessEventType.RunCompleted:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _host.SetStatusMessage(agentEvent.Run?.CompletionReason is { Length: > 0 } reason ? reason : "运行完成。");
                });
                break;
            case AgentHarnessEventType.RunUsage:
                // 2026-08-05: per-call token usage (prompt /
                // completion / cached). Forward to the
                // host so the activity feed can append a
                // "X tokens, Y% cache 命中" footer and
                // the status bar can show the cache
                // ring. Marshal to the UI thread
                // because the host's setters are
                // observable-collection-bound and Avalonia
                // throws on cross-thread mutations.
                if (agentEvent.Usage is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _host.RecordRunUsage(agentEvent.Usage);
                    });
                }
                break;
        }
    }

    // Marshal a plan refresh to the UI thread. The harness event
    // loop yields on whatever thread the underlying LLM stream
    // resumes on (typically a thread-pool thread after an HTTP
    // read), but the host's PlanItems is an ObservableCollection
    // bound to a panel in the MainWindow XAML. Mutating it from a
    // non-UI thread is unsafe — Avalonia either throws
    // "Collection was modified during enumeration" on the render
    // side, or silently drops the change and the user sees a
    // stale plan. Every other host-state mutation in
    // ApplyAgentEventAsync is already wrapped in an explicit
    // Dispatcher.UIThread.InvokeAsync for the same reason; this
    // helper keeps the plan dispatch next to the others instead
    // of three identical lambda blocks scattered through the
    // switch.
    private async Task UpdatePlanOnUiThreadAsync(AIChat.Domain.Chat.AgentPlan? plan)
    {
        await Dispatcher.UIThread.InvokeAsync(() => _host.UpdatePlan(plan));
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

    // Build the "本次运行" summary the host drops into the
    // activity feed right after a run lands. Keeps to one line
    // of plain text so the system bubble stays scannable: files
    // / tools / duration. Explorer / worker sub-agent counts +
    // verification results are surfaced when the run actually
    // used them — silent otherwise so a simple chat exchange
    // doesn't look heavier than it was.
    //
    // The caller passes isReadOnly so a no-write run that
    // touched zero files can carry a "只读" tag — the user sent
    // a refactor prompt, the agent did all the planning,
    // nothing landed, and "改 0 个文件" by itself doesn't tell
    // them whether to flip read-only off and retry or whether
    // the agent decided the task was already done. The tag
    // makes the cause visible without an extra system bubble.
    public static string BuildRunSummary(AIChat.Domain.Chat.AgentRun run, bool isReadOnly = false, ChatUsage? usage = null)
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
        if (!string.IsNullOrWhiteSpace(run.Model))
        {
            parts.Add(run.Model);
        }
        if (fileChangeCount > 0)
        {
            parts.Add($"改 {fileChangeCount} 个文件");
        }
        if (toolCount > 0)
        {
            parts.Add($"用 {toolCount} 次工具");
        }
        if (run.ModelCallCount > 0)
        {
            parts.Add($"模型 {run.ModelCallCount} 轮");
        }
        if (usage is not null && usage.PromptTokens > 0)
        {
            // 2026-08-05: surface the platform's actual
            // token usage + prompt-cache hit rate so
            // a daily driver can see "182 tokens, 64%
            // cache 命中" after every run. Cached
            // reads are 1/5 input price on M3, so the
            // cache% is the cheapest ROI number on
            // the screen — a user with 50%+ cache
            // hit is paying roughly half the input
            // cost of one without cache.
            if (usage.CachedTokens > 0)
            {
                parts.Add($"{usage.TotalTokens:N0} tokens · {usage.CacheHitPercent}% cache 命中");
            }
            else
            {
                parts.Add($"{usage.TotalTokens:N0} tokens");
            }
        }
        else if (run.ContextEstimatedTokens > 0)
        {
            parts.Add($"输入约 {run.ContextEstimatedTokens:N0} tokens");
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

    // Wave 2: v1 模型下,写新 session 到 repo + 把当前 workspace 持久化。
    // CurrentProjectSessions 是 sidebar 已加载的当前项目的 sessions。
    private async Task SaveSessionAndProjectAsync(ChatSession conversation, bool isExistingConversation)
    {
        var sidebar = _sidebar;
        if (sidebar.CurrentProject is null)
        {
            return;
        }

        conversation.UpdatedAt = DateTimeOffset.Now;
        var sessions = sidebar.CurrentProjectSessions.ToList();
        if (!isExistingConversation && sessions.All(item => item.Id != conversation.Id))
        {
            sessions.Add(conversation);
        }
        else
        {
            // 替换 in-memory 列表里的同 id session
            var idx = sessions.FindIndex(s => s.Id == conversation.Id);
            if (idx >= 0)
            {
                sessions[idx] = conversation;
            }
        }

        sidebar.CurrentProject.UpdatedAt = DateTimeOffset.Now;

        // 写 workspaces + sessions
        var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
        var wsIndex = workspaces.FindIndex(w => w.Id == sidebar.CurrentProject.Id);
        if (wsIndex >= 0)
        {
            workspaces[wsIndex] = sidebar.CurrentProject;
        }
        else
        {
            workspaces.Add(sidebar.CurrentProject);
        }
        await _repository.SaveWorkspacesAsync(workspaces);
        await _repository.SaveSessionsAsync(sessions);
        sidebar.UpdateCurrentProjectSessions(sessions);

        _conversationList.Refresh(sidebar.CurrentProject, sessions, conversation.Id);
    }

    private static string BuildProjectSnapshot(WorkspaceProject project, IReadOnlyList<ChatSession> sessions)
    {
        var snapshot = ProjectLoadSnapshotBuilder.Build(project, sessions);
        return string.Join(Environment.NewLine, [
            snapshot.HealthText,
            snapshot.ProfileText,
            snapshot.ActivityText,
            snapshot.RecommendationText
        ]);
    }

    private async Task PersistConversationAsync(
        WorkspaceProject project,
        ChatSession conversation,
        bool isExistingConversation)
    {
        await SaveSessionAndProjectAsync(conversation, isExistingConversation);
    }

    private async Task PersistConversationSafelyAsync(
        WorkspaceProject project,
        ChatSession conversation,
        bool isExistingConversation)
    {
        try
        {
            await PersistConversationAsync(project, conversation, isExistingConversation);
        }
        catch
        {
            // Preserve the original run error/cancellation while making the
            // separate history-save failure visible to the user.
            await _host.ReportConversationPersistenceFailureAsync();
        }
    }
}

public sealed record RunTerminalPresentation(
    string ActivityStatus,
    string StatusMessage,
    string FallbackDetail);
