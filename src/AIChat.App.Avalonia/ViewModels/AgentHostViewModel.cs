using System.Collections.ObjectModel;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Projects;
using AIChat.Application.Prompting;
using AIChat.Application.Sources;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Application.Verification;
using AIChat.Application.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Domain.Sources;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the agent-run state on behalf of the host:
// - the user input (DraftPrompt) + pending image attachments
// - the per-run CancellationTokenSource and the last-sent prompt
//   (so RetryLastTask can re-send without retyping)
// - the run state (IsRunning, LastAssistantStatus, InputTokens) that
//   the status bar / send-stop button / retry button bind to
// - the in-flight plan panel (PlanItems) and sub-agent list
//   (SubAgentRuns) the agent runner pushes events into
// - SendTask / StopTask / RetryLastTask commands + context-budget
//   recompute (re-runs the file index + context router on every
//   prompt keystroke / project switch / no-write toggle)
//
// Extracted from MainWindowViewModel during the 1.0 refactor so
// the host stops carrying 450+ lines of unrelated run-loop plumbing.
// The host retains the cross-cutting concerns (activity feed wiring,
// approval bubbles, sidebar / conversation list subscriptions,
// settings surface, modals) and talks to this VM through:
// - AgentHost property (XAML binds through it)
// - small Action/Func callbacks for the host-owned shared state
//   the agent runner writes into (setStatusMessage → host.StatusMessage,
//   getSettings → host._settings, getNoWriteMode → host.NoWriteMode).
//   Those three are the only state the host and the run state share;
//   everything else the runner touches lives here.
public sealed partial class AgentHostViewModel : ViewModelBase
{
    // ---- Shared host callbacks (the only state the host owns) ----

    private readonly Action<string> _setStatusMessage;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<bool> _getNoWriteMode;
    private readonly Func<Action, Task> _dispatchToUiAsync;
    // True while a connection test (⌘T / "测试当前模型") is in
    // flight. Surfaced here so CanSendTask can disable the send
    // button — otherwise the user could start a second agent run
    // while the test is still in flight, racing two requests
    // against the same provider. The flag lives on the host
    // because the test is fired from ProviderConfigViewModel via
    // TestStarted/TestCompleted events; the agent loop never sets
    // it directly.
    private readonly Func<bool> _getIsProviderTesting;

    // ---- Host-side collaborators (still singletons, owned by the host) ----

    private readonly AgentRunnerViewModel _agentRunner;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ActivityFeedViewModel _activityFeed;
    private readonly IToastService _toast;
    private readonly IAppRepository _repository;
    private readonly IApprovalService _approval;
    private readonly ISourceRegistry _sourceRegistry;
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly ConversationListViewModel _conversationList;
    private readonly ProjectVerificationExecutor _verificationExecutor = new();
    private readonly InputArtifactFileStore _artifactFileStore;

    // CTS for the currently running agent task. New SendTaskCommand
    // runs replace it; StopTaskCommand cancels it. The token is
    // passed into AgentRunner.RunAsync and forwarded to AgentHarness
    // so cancellation halts the inner loop at the next await point.
    private CancellationTokenSource? _runCts;

    // CTS for the in-flight context-input recompute. Every caller
    // fires RecomputeContextInputTokensAsync without awaiting (it's
    // status-bar polish, not a hard dependency), and the recompute
    // path is the kind that thrashes on rapid keystroke streams
    // (each keystroke shifts the goal, which shifts the context
    // router's pick-list). The CTS lets each new call cancel the
    // in-flight one before it starts a new one. Read / write is
    // guarded by _recomputeLock so a 200ms debounce Task.Delay
    // running on a thread-pool thread doesn't race a fresh
    // caller swapping the CTS.
    private CancellationTokenSource? _recomputeCts;
    private readonly object _recomputeLock = new();

    // The last user prompt that survived validation. Used by
    // RetryLastTask so a failed/cancelled run can be re-sent
    // without retyping.
    private string _lastUserPrompt = "";
    private string _lastRunId = "";
    private string _pendingContinuationRunId = "";
    private string _pendingRetriedRunId = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunVerificationCommand))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanRunVerification))]
    private bool isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string lastAssistantStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunVerificationCommand))]
    [NotifyPropertyChangedFor(nameof(CanRunVerification))]
    private bool isVerifying;

    public bool CanRetry =>
        !string.IsNullOrEmpty(_lastUserPrompt)
        && !IsRunning
        && (LastAssistantStatus is "失败" or "已停止" or "预算暂停");

    // 1.0.1: "regenerate" — same gate as
    // CanRetry minus the terminal-status
    // check, so the button is enabled
    // for successful runs too (the
    // "I want a different response"
    // flow). The AI bubble's
    // 重新生成 button binds to this
    // so the affordance surfaces for
    // every completed / failed /
    // stopped run.
    public bool CanRegenerate =>
        !string.IsNullOrEmpty(_lastUserPrompt)
        && !IsRunning;

    public bool CanRunVerification =>
        !IsRunning && !IsVerifying &&
        !string.IsNullOrWhiteSpace(_sidebar.CurrentProject?.TryGetPrimaryPath()) &&
        _sidebar.CurrentProject.VerificationCommands.Count > 0;

    [ObservableProperty]
    private string draftPrompt = "";

    // Estimated input tokens for the current prompt against the
    // current project (context router output + prompt + system/tool
    // schema budget). Recomputed on project change, on every prompt
    // keystroke, and at run start so the status-bar context meter
    // is always current. The runner pushes a final value via
    // setInputTokens at BeginRun so the meter reflects the actual
    // request the agent will send (not just the host's pre-build
    // estimate).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextBudgetPercent))]
    [NotifyPropertyChangedFor(nameof(ContextBudgetWidthInMini))]
    [NotifyPropertyChangedFor(nameof(ContextBudgetDetails))]
    private int inputTokens;

    public int ContextBudgetPercent =>
        (int)Math.Clamp(InputTokens * 100.0 / Math.Max(1, _getSettings().ModelContextLimit), 0, 100);

    public string ContextBudgetDetails =>
        $"上下文估算：约 {InputTokens:N0} / {Math.Max(1, _getSettings().ModelContextLimit):N0} tokens ({ContextBudgetPercent}%)\n" +
        "这是本地路由估算，不是提供方计费 usage。";

    // 2026-08-05: actual billing usage from the
    // platform (the InputTokens above is a pre-build
    // estimate). Set per model call from the
    // streaming response's final usage block —
    // carries the prompt / completion / cached
    // breakdown. Surfaced in the activity feed
    // footer (e.g. "182 tokens, 64% cache 命中")
    // and in the status bar context details.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRunUsageText))]
    [NotifyPropertyChangedFor(nameof(LastRunCacheHitPercent))]
    private ChatUsage? lastRunUsage;

    public string LastRunUsageText
    {
        get
        {
            var usage = LastRunUsage;
            if (usage is null || usage.PromptTokens == 0)
            {
                return "本轮未拿到 usage（平台未返回或请求失败）。";
            }
            var cacheNote = usage.CachedTokens > 0
                ? $"，{usage.CacheHitPercent}% 命中 cache"
                : "";
            return $"本轮 {usage.TotalTokens:N0} tokens (输入 {usage.PromptTokens:N0} / 输出 {usage.CompletionTokens:N0}{cacheNote})。";
        }
    }

    public double LastRunCacheHitPercent => LastRunUsage?.CacheHitPercent ?? 0;

    // 2026-08-05: visibility gate for the
    // status-bar cache hint chip. Returns
    // true when the last completed run
    // actually hit the platform's prompt
    // cache — a user on M3 with a 60%+
    // hit rate should see the hint
    // permanently lit (one of the cheapest
    // ROI numbers on the screen, since
    // cached reads are 1/5 input price).
    // Returns false when the run had no
    // usage block, no cached tokens, or
    // the parser returned 0% — the chip
    // is hidden rather than rendered as
    // "cache 0%" noise.
    public bool HasCacheHint => LastRunUsage is not null
        && LastRunUsage.CachedTokens > 0
        && LastRunCacheHitPercent > 0;

    // Width in DIPs for the inline cache
    // ring. 50px wide, so 0% = 0px,
    // 100% = 50px.
    public double LastRunCacheHitWidth =>
        Math.Clamp(LastRunCacheHitPercent * 0.5, 0, 50);

    // Display text for the status-bar
    // chip. Truncates the percent to 0
    // decimals when it's a clean
    // integer (60.0% → "60%") and shows
    // 1 decimal otherwise ("64.4%"). The
    // format is the same the activity
    // feed's "本次运行" summary uses so a
    // user scanning both surfaces sees
    // the same number.
    public string LastRunCacheHitText
    {
        get
        {
            var percent = LastRunCacheHitPercent;
            if (percent <= 0)
            {
                return "";
            }
            // 0.05 rounds to 0.1 (1 decimal
            // place) for a clean visual
            // when the percent is < 10. The
            // activity feed uses the same
            // rounding so the two surfaces
            // never disagree.
            return Math.Abs(percent - Math.Round(percent)) < 0.05
                ? $"{(int)Math.Round(percent)}% cache"
                : $"{percent:F1}% cache";
        }
    }

    // 2026-08-05: agent runner callback. Called from
    // the UI thread (the runner marshals the
    // AgentHarnessEventType.RunUsage handler through
    // Dispatcher.UIThread.InvokeAsync before calling
    // this). Updates LastRunUsage; the activity feed
    // + status bar both bind to the derived
    // properties.
    public void RecordRunUsage(ChatUsage usage)
    {
        LastRunUsage = usage;
    }

    // Width in DIPs for the inline context meter in the status
    // bar. The mini bar is 80px wide so the percent→width factor
    // is 0.8.
    public double ContextBudgetWidthInMini => Math.Max(0, ContextBudgetPercent * 0.8);

    // Pending image attachments (paste-into-prompt). The user
    // pastes an image with ⌘V while the prompt is focused; the
    // view code-behind saves the bitmap and adds a row to this
    // collection. The thumbnails show above the composer; on send
    // the host materialises InputArtifact records and wires them
    // into the chat request.
    public PendingAttachmentsViewModel PendingAttachments { get; } = new();

    // Plan + sub-agent display state. Updated by the agent runner
    // via the updatePlan / upsertSubAgent / clearSubAgentRuns
    // callbacks when the harness emits StepAdded / SubAgentStarted
    // / SubAgentCompleted events. PlanItems is the
    // ItemsControl-bound ObservableCollection in the plan panel;
    // SubAgentRuns is the sub-section below it. HasPlan /
    // HasSubAgentRuns are the IsVisible bindings for the whole
    // panel and the sub-section respectively.
    public ObservableCollection<PlanItemViewModel> PlanItems { get; } = [];
    public bool HasPlan => PlanItems.Count > 0;
    public int PlanCompletedCount => PlanItems.Count(item => item.IsCompleted);
    public string PlanProgressText => $"{PlanCompletedCount} / {PlanItems.Count}";

    public ObservableCollection<SubAgentRunViewModel> SubAgentRuns { get; } = [];
    // The plan panel's sub-section binds IsVisible to this. HasPlan
    // has the same story (collection-only changes don't fire
    // PropertyChanged on derived bools); re-raise it in
    // UpdatePlan / UpsertSubAgentRun / clearSubAgentRuns so the
    // panel collapses when the run finishes.
    public bool HasSubAgentRuns => SubAgentRuns.Count > 0;

    public AgentHostViewModel(
        IChatCompletionService chatService,
        AgentToolRegistry toolRegistry,
        IApprovalService approval,
        IAppRepository repository,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        ActivityFeedViewModel activityFeed,
        IToastService toast,
        ISourceRegistry sourceRegistry,
        Action<string> setStatusMessage,
        Func<AppSettings> getSettings,
        Func<bool> getNoWriteMode,
        Func<bool> getIsProviderTesting,
        Func<Action, Task>? dispatchToUiAsync = null,
        InputArtifactFileStore? artifactFileStore = null)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _approval = approval;
        _repository = repository;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _activityFeed = activityFeed;
        _toast = toast;
        _sourceRegistry = sourceRegistry;
        _setStatusMessage = setStatusMessage;
        _getSettings = getSettings;
        _getNoWriteMode = getNoWriteMode;
        _getIsProviderTesting = getIsProviderTesting;
        _artifactFileStore = artifactFileStore ?? new InputArtifactFileStore();
        _dispatchToUiAsync = dispatchToUiAsync ?? (async action =>
            await Dispatcher.UIThread.InvokeAsync(action));

        // Construct the agent runner last so the runner's
        // "host" reference is the fully-initialised AgentHost.
        // The runner calls back into our fields + methods (no
        // longer through Action/Func delegates) for everything
        // except the three host-owned primitives bridged above.
        _agentRunner = new AgentRunnerViewModel(
            chatService,
            toolRegistry,
            approval,
            repository,
            activityFeed,
            sidebar,
            conversationList,
            this);

        _sidebar.ProjectSelected += OnProjectSelected;
        _sidebar.ProjectAdded += OnProjectAdded;
    }

    private void OnProjectSelected(object? sender, ProjectSelectionChangedEventArgs args)
    {
        _ = RecomputeContextInputTokensAsync(DraftPrompt);
        _setStatusMessage(args.StatusMessage);
        OnPropertyChanged(nameof(CanRunVerification));
        RunVerificationCommand.NotifyCanExecuteChanged();
    }

    private void OnProjectAdded(object? sender, ProjectAddedEventArgs args)
    {
        _ = RecomputeContextInputTokensAsync(DraftPrompt);
        OnPropertyChanged(nameof(CanRunVerification));
        RunVerificationCommand.NotifyCanExecuteChanged();
    }

    partial void OnDraftPromptChanged(string value)
    {
        _ = RecomputeContextInputTokensAsync(value);
        // 1.0.1: the send button is CanExecute-gated
        // on !string.IsNullOrWhiteSpace(DraftPrompt)
        // (see CanSendTask). Re-raise so a user
        // emptying the composer sees the send
        // button dim out without having to first
        // change some other piece of state.
        SendTaskCommand.NotifyCanExecuteChanged();
    }

    // 1.0.1: insert a Source's @-reference at the given
    // caret index in DraftPrompt. The previous behaviour
    // (insert via EnvironmentPanel's per-row "引用"
    // button) always *appended* to the end of the
    // prompt — fine for a one-line draft, but a daily-
    // driver user mid-paragraph who jumps to the side
    // panel to grab a source reference, clicks 引用,
    // and watches the text land at the very end of the
    // prompt (not where the caret was) had to manually
    // cut and paste it back. This method takes the
    // caretIndex from the XAML (the TextBox's live
    // CaretIndex at click time) and splices the
    // reference in place.
    //
    // Dedupes against the existing prompt (matching the
    // previous append behaviour): clicking the button
    // twice in a row with the caret parked at the same
    // spot is a no-op the second time, so an over-eager
    // click doesn't land two copies of the same
    // reference text. Caret clamps to the current
    // DraftPrompt length so a stale caret from a longer
    // draft can't throw — we just land at the end.
    // Surface the live source registry so the XAML key
    // handler can run the autocomplete lookup without
    // reaching into private fields. Read-only view — the
    // registry itself is mutated by
    // EnvironmentPanelViewModel.
    public IReadOnlyList<AIChat.Domain.Sources.Source> SourcesForAutocomplete =>
        _sourceRegistry.Sources;

    public void InsertSourceReferenceAtCaret(
        AIChat.Domain.Sources.Source source,
        int caretIndex)
    {
        if (source is null)
        {
            return;
        }
        var reference = AIChat.Application.Sources.SourceReferenceParser.FormatReference(source);
        var current = DraftPrompt ?? "";
        if (current.Contains(reference, StringComparison.Ordinal))
        {
            return;
        }
        // Caret may be stale if the user deleted text
        // after a previous click; clamp so we don't
        // ArgumentOutOfRange the splice.
        var safeCaret = Math.Clamp(caretIndex, 0, current.Length);
        var before = current[..safeCaret];
        var after = current[safeCaret..];
        // Insert a leading space when the splice
        // boundary has a non-whitespace char on the
        // left (so we don't fuse "@web:abc" onto the
        // previous word like "hello@web:abc") and a
        // trailing space when the right side starts
        // with a non-whitespace char (so the
        // reference doesn't fuse onto the next word
        // either).
        var needsLeading = before.Length > 0 && !char.IsWhiteSpace(before[^1]);
        var needsTrailing = after.Length > 0 && !char.IsWhiteSpace(after[0]);
        var leading = needsLeading ? " " : "";
        var trailing = needsTrailing ? " " : "";
        DraftPrompt = before + leading + reference + trailing + after;
    }

    public void RecomputeOnNoWriteModeChanged()
    {
        // Called by the host's OnNoWriteModeChanged partial. The
        // no-write toggle shifts which tools the agent can see,
        // which shifts the system prompt size, which shifts the
        // context estimate — re-run on toggle.
        _ = RecomputeContextInputTokensAsync(DraftPrompt);
    }

    // Read-throughs the agent runner uses to reach the
    // host-owned state. Keep the runner's dependency surface
    // narrow — the runner sees only this VM, the VM owns the
    // read through to the host.
    public AppSettings GetSettings() => _getSettings();
    public bool GetNoWriteMode() => _getNoWriteMode();
    public void SetStatusMessage(string value) => _setStatusMessage(value);

    [RelayCommand(CanExecute = nameof(CanSendTask))]
    private async Task SendTaskAsync()
    {
        // The whole send path needs to surface failures to the
        // user instead of letting them escape the RelayCommand. The
        // ⌘↵ keyboard path goes through MainWindow.SafeRun
        // (try/catch wrapper), but the XAML send button calls
        // Command.Execute directly — any uncaught exception in the
        // async lambda becomes an unhandled-task exception on the
        // dispatcher. The inner try/catch below only covers the
        // agent run; promote the body into a try/finally that
        // always restores IsRunning and reset the per-send CTS so
        // a partial failure can't leave the host in a stuck-running
        // state.
        try
        {
            var prompt = DraftPrompt.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _activityFeed.Add("需要任务", "先描述你希望 AIChat 完成什么。", "等待");
                _setStatusMessage("请先输入任务。");
                return;
            }

            // Slash commands (/clear, /help, /status, /new, /copy)
            // short-circuit the agent loop. The handler renders its
            // result as a system bubble in the activity feed and
            // clears the draft. The handler is registered by the
            // host (which still owns the /status surface the handler
            // reads); if the host hasn't registered one, treat the
            // prompt as non-slash and fall through to the agent.
            if (_slashHandler is not null)
            {
                var (handled, slashResult) = await _slashHandler(prompt);
                if (handled)
                {
                    DraftPrompt = "";
                    if (slashResult is not null)
                    {
                        _activityFeed.Add(slashResult.Title, slashResult.Body, "系统");
                        _setStatusMessage(slashResult.Title + "。");
                    }
                    return;
                }
            }

            // @file references: pull @path tokens out of the
            // prompt, read the file contents, and drop a system
            // bubble per attachment so the user can see what got
            // inlined. Warnings (file not found, too large) render
            // as their own system bubble so the user gets feedback
            // rather than a silent skip. The cleaned prompt (with
            // the @tokens stripped) plus a context block listing
            // the attached file contents is what the agent sees.
            var projectRoot = _sidebar.CurrentProject?.TryGetPrimaryPath();
            var parsed = PromptAttachmentParser.Parse(prompt, projectRoot);
            foreach (var attachment in parsed.Attachments)
            {
                var preview = attachment.Content.Length > 200
                    ? attachment.Content[..200] + "…"
                    : attachment.Content;
                _activityFeed.Add(
                    $"📎 {attachment.ResolvedPath}  ({attachment.ByteCount} 字节)",
                    preview,
                    "附件");
            }
            foreach (var warning in parsed.Warnings)
            {
                _activityFeed.Add(
                    $"⚠ {warning.OriginalToken}",
                    warning.Message,
                    "附件");
            }

            // Build the prompt the agent actually sees: cleaned
            // user question + a labelled context block listing
            // every attached file's content. Empty if the prompt
            // was just @file references — in that case the user
            // has seen the system bubbles; nothing more to do.
            if (parsed.Attachments.Count > 0)
            {
                var contextBlock = new System.Text.StringBuilder();
                contextBlock.AppendLine("Attached files (use these for context):");
                foreach (var attachment in parsed.Attachments)
                {
                    contextBlock.AppendLine();
                    contextBlock.AppendLine($"--- {attachment.ResolvedPath} ---");
                    contextBlock.Append(attachment.Content);
                }
                prompt = string.IsNullOrWhiteSpace(parsed.CleanPrompt)
                    ? contextBlock.ToString()
                    : contextBlock + Environment.NewLine + Environment.NewLine + parsed.CleanPrompt;
            }
            else
            {
                prompt = parsed.CleanPrompt;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                // The prompt was just @file references; nothing
                // to send to the agent. DraftPrompt is already
                // cleared below.
                _setStatusMessage("已附加文件。");
                DraftPrompt = "";
                return;
            }

            _ = RecomputeContextInputTokensAsync(prompt);

            var settings = _getSettings();
            var effectiveSettings = ProviderSettingsService.CreateEffectiveSettings(settings, settings.Temperature);
            var validation = ProviderConfigurationValidator.ValidateEffectiveSettings(effectiveSettings);
            if (!validation.IsValid || effectiveSettings is null)
            {
                var message = validation.Errors.FirstOrDefault()?.Message ?? "发送前需要配置模型密钥。";
                _activityFeed.Add("需要配置模型", message, "已阻止");
                _setStatusMessage(message);
                return;
            }

            if (_sidebar.CurrentProject is null || string.IsNullOrWhiteSpace(_sidebar.CurrentProject.TryGetPrimaryPath()))
            {
                _activityFeed.Add("需要项目", "发送前请先选择或初始化项目。", "已阻止");
                _setStatusMessage("当前没有可运行的项目。");
                return;
            }

            // Promote any pasted-image attachments to
            // InputArtifacts on the current project so the agent
            // loop can pick them up (AgentRequestFactory reads
            // project.InputArtifacts and attaches image content
            // parts to the latest user message for vision-capable
            // models). The PNG files are already on disk in the
            // pending-attachments folder — we just record them and
            // clean up the UI strip. Project-level scope means the
            // artifacts survive the conversation boundary.
            if (PendingAttachments.Count > 0)
            {
                await PromotePendingAttachmentsAsync(_sidebar.CurrentProject);
            }

            // Wave 7 (parity plan §7 Wave 7) third slice:
            // resolve any @-references in the prompt
            // (e.g. "@web:abc123" / "@clip:def456") and
            // attach the referenced Source bodies as
            // InputArtifacts. The @-text stays in the
            // prompt so the user / agent can see what
            // was referenced; the body is duplicated as
            // a separate artifact so the agent loop can
            // render it as a system-prompt section via
            // AgentRequestFactory.
            if (prompt.Contains('@'))
            {
                await PromoteSourceReferencesAsync(prompt, _sidebar.CurrentProject);
            }

            // Replace any prior CTS. A new run cancels nothing —
            // the user already chose to start a fresh one, so the
            // old token has no listener any more.
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();

            // Remember the prompt so a failed/cancelled run can be
            // retried without retyping. RetryLastTask only fires
            // when this is set AND the last run's status was 失败
            // or 已停止 (CanRetry).
            _lastUserPrompt = prompt;

            try
            {
                var continuedFromRunId = _pendingContinuationRunId;
                var retriedFromRunId = _pendingRetriedRunId;
                _pendingContinuationRunId = "";
                _pendingRetriedRunId = "";
                await _agentRunner.RunAsync(
                    prompt,
                    effectiveSettings,
                    continuedFromRunId,
                    retriedFromRunId,
                    _runCts.Token);
            }
            catch (OperationCanceledException)
            {
                // StopTaskCommand cancelled the run. The agent
                // runner has already flipped IsRunning to false and
                // updated the activity item's status to "已停止";
                // just record the user-visible status, drop a
                // toast so the user notices even if the window
                // isn't focused, and move on.
                _setStatusMessage("已停止。");
                _toast.Show("任务已停止。", ToastLevel.Warning);
            }
            catch (Exception ex)
            {
                _setStatusMessage("请求失败。");
                _toast.Show(ex.Message, ToastLevel.Error);
            }
        }
        catch (Exception ex)
        {
            // Catch-all for any failure before the agent run kicks
            // off (file copy for a pasted image, settings file
            // write for the project, …). Without this the user
            // would see a crashed app or a silently stuck IsRunning
            // state. Show the same surface the inner catch uses
            // so the failure path is consistent.
            _setStatusMessage("请求失败。");
            _toast.Show(ex.Message, ToastLevel.Error);
        }
        finally
        {
            // Always release the per-send CTS so a failed run
            // doesn't strand the host. The agent runner flips
            // IsRunning back to false in its own finally; this
            // finally is the outer guarantee that even a pre-run
            // throw leaves the host in a clean state.
            _runCts?.Dispose();
            _runCts = null;
        }

        // 1.0.1: drain the follow-up queue
        // if the user pressed 追加要求
        // during the run. We check
        // _pendingFollowup here (after
        // IsRunning has been flipped back
        // to false by the runner's
        // finally, so the auto-continuation
        // starts a clean run). The
        // queue is one-deep on purpose:
        // a daily-driver user who clicks
        // 追加要求 while a run is in
        // flight is signalling "one more
        // thing to handle after this
        // one", not "I want to chain
        // eight more runs from this
        // button". Multiple presses
        // over-write the last queued
        // prompt (a follow-up to a
        // follow-up is what the user
        // actually means when they
        // press twice).
        if (!string.IsNullOrWhiteSpace(PendingFollowup))
        {
            var queued = PendingFollowup;
            PendingFollowup = null;
            DraftPrompt = queued;
            // Fire-and-forget the next
            // send. We can't await
            // SendTaskAsync from within
            // itself (would recurse on
            // the same call site) and
            // we don't want a synchronous
            // call to block the cleanup
            // path. The status bar +
            // activity-feed "+ 追加"
            // bubble the user knows
            // the continuation kicked
            // off.
            _ = SendTaskAsync();
        }
    }

    // 1.0.1: a daily-driver user who's
    // running a long task and notices
    // they need to add context can't
    // wait for the run to finish before
    // queuing a follow-up. Queue the
    // prompt here; the post-run cleanup
    // in SendTaskAsync fires a fresh
    // send with the queued prompt.
    //
    // No-op when the run isn't in flight
    // — the 追加要求 button is only
    // visible when IsRunning, but the
    // public method also guards in case
    // a future caller races the
    // IsRunning flip.
    //
    // Returns true if the prompt was
    // accepted into the queue, false if
    // it was rejected (no run in flight,
    // empty prompt, or already-queued
    // prompt is being overwritten).
    public bool EnqueueFollowup(string? prompt)
    {
        if (!IsRunning) return false;
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        PendingFollowup = prompt.Trim();
        _activityFeed.Add(
            "已暂存追加",
            $"将在当前 run 结束后自动发送：\"{(PendingFollowup.Length > 60 ? PendingFollowup[..60] + "…" : PendingFollowup)}\"",
            "继续");
        return true;
    }

    // 1.0.1: 1-deep follow-up queue. See
    // EnqueueFollowup for why this is a
    // single string and not a Queue<string>.
    // Promoted to [ObservableProperty] so
    // HasPendingFollowup re-raises on
    // every write — the 追加要求 button
    // label binds to HasPendingFollowup
    // and the visual must flip the
    // moment the queue is filled or
    // drained, not lag behind.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingFollowup))]
    private string? _pendingFollowup;

    // 1.0.1: visible state of the
    // 1-deep follow-up queue. The
    // 追加要求 button's label flips
    // between "追加要求" (no queue) and
    // "已暂存" (queue occupied) so the
    // user can tell whether their
    // follow-up has been recorded. A
    // daily-driver user who clicks
    // 追加要求 during a long run may
    // wonder "did the click land?" —
    // the label flip is the affirmative
    // answer, no need to scroll up to
    // the system bubble to confirm.
    // Drained in the post-run cleanup
    // path so the label flips back to
    // "追加要求" the moment the next
    // run starts.
    public bool HasPendingFollowup => !string.IsNullOrWhiteSpace(PendingFollowup);

    // 1.0.1: detect an in-progress
    // @-reference at the caret. Pure
    // function — the XAML key handler
    // calls it on Tab / Enter to look
    // up the first matching Source for
    // auto-completion. The result is a
    // Source (when matched) or null
    // (when there's no @<kind>:<partial>
    // at the caret, or the partial
    // doesn't match any registry
    // entry). The @<kind>:<partial>
    // grammar is the same one
    // SourceReferenceParser.Parse uses
    // for the send path, so what
    // completes here is exactly what
    // gets sent.
    //
    // Pure function on (text,
    // caretIndex, registry) so unit
    // tests don't need a live
    // Avalonia TextBox.
    public static Source? TrySuggestAtCompletion(
        string text,
        int caretIndex,
        IReadOnlyList<Source> sources)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
        {
            return null;
        }
        // Walk backwards from the
        // caret to find the most recent
        // '@' that isn't preceded by a
        // word character (so the email
        // "user@host" case doesn't
        // trigger on a real email).
        var atIndex = -1;
        for (var i = caretIndex - 1; i >= 0; i--)
        {
            if (text[i] == '@')
            {
                // Reject if the
                // character
                // before @ is a
                // word char (the
                // email case).
                if (i > 0 && char.IsLetterOrDigit(text[i - 1]))
                {
                    return null;
                }
                atIndex = i;
                break;
            }
            // A space or newline
            // breaks the @-run
            // — we only consider
            // the contiguous
            // word the caret is
            // inside.
            if (char.IsWhiteSpace(text[i]))
            {
                return null;
            }
        }
        if (atIndex < 0)
        {
            return null;
        }
        // Extract the @-run from
        // atIndex to caretIndex.
        // Expected shape:
        //   @                  → empty
        //   @web               → kind
        //   @web:              → kind
        //   @web:abc           → kind+partial
        //   @WEB:abc (upper)   → kind
        //   @web:abc123        → longer partial
        var fragment = text[atIndex..caretIndex];
        if (!System.Text.RegularExpressions.Regex.IsMatch(
            fragment, @"^@([A-Za-z]+)(?::([A-Za-z0-9]*))?$"))
        {
            return null;
        }
        // Match the kind +
        // optional partial-id
        // against the registry.
        // Case-insensitive kind +
        // case-insensitive id prefix
        // (matches the parser).
        var match = System.Text.RegularExpressions.Regex.Match(
            fragment, @"^@(?<kind>[A-Za-z]+)(?::(?<id>[A-Za-z0-9]*))?$");
        var kind = match.Groups["kind"].Value;
        var partial = match.Groups["id"].Value;
        foreach (var source in sources)
        {
            if (!string.Equals(source.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (partial.Length == 0)
            {
                return source;
            }
            if (source.Id.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }
        return null;
    }

    // 1.0.1: built-in slash
    // commands surfaced as a
    // static list for the Tab
    // autocomplete path. The
    // SlashCommandHandler switch
    // is the runtime dispatcher
    // — this list is the
    // discoverability surface
    // (typing "/co" + Tab should
    // land on /copy). Keep the
    // two in sync: the test
    // SlashCommandTests_Help_ListsEveryBuiltInCommand
    // pins /help to mention every
    // command, and a future
    // /autocomplete-style
    // listing can read this same
    // array.
    public static IReadOnlyList<string> BuiltInSlashCommands { get; } = new[]
    {
        "/clear",
        "/new",
        "/help",
        "/status",
        "/memory",
        "/git",
        "/git-status",
        "/copy",
        "/search",
    };

    // 1.0.1: slash command Tab
    // completion. Pure function
    // on (text, caretIndex,
    // BuiltInSlashCommands) so
    // unit tests don't need a
    // live Avalonia TextBox. The
    // result is the first
    // command that starts with
    // the fragment the user has
    // typed after a `/` at the
    // caret (case-insensitive),
    // or null when there's no
    // `/<partial>` at the caret
    // (or the partial doesn't
    // match any built-in
    // command). The grammar
    // mirrors TrySuggestAtCompletion
    // for the @-reference case
    // — a `/` is only treated
    // as a command-start if it
    // sits at the start of a
    // word (not preceded by a
    // word character; the
    // user-typed "foo/bar" path
    // should not trigger).
    public static string? TrySuggestSlashCompletion(
        string text,
        int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
        {
            return null;
        }
        // Walk backward from the
        // caret to find the most
        // recent `/` that isn't
        // preceded by a word char
        // (the path / URL case
        // shouldn't trigger). We
        // also require the `/` to
        // be at the START of the
        // prompt or immediately
        // after whitespace, so
        // "/copy" in the middle
        // of a sentence doesn't
        // get a Tab-completion
        // suggestion. (Slash
        // commands are whole-line
        // affordances; the parser
        // already special-cases
        // them only when the
        // prompt starts with `/`.)
        var slashIndex = -1;
        for (var i = caretIndex - 1; i >= 0; i--)
        {
            if (text[i] == '/')
            {
                if (i > 0 && !char.IsWhiteSpace(text[i - 1]))
                {
                    return null;
                }
                slashIndex = i;
                break;
            }
            // A space or newline
            // breaks the /-run —
            // we only consider the
            // contiguous word the
            // caret is inside.
            if (char.IsWhiteSpace(text[i]))
            {
                return null;
            }
        }
        if (slashIndex < 0)
        {
            return null;
        }
        var fragment = text[slashIndex..caretIndex];
        // The fragment includes the
        // leading `/` so the
        // StartsWith check below is
        // comparing "/" + name-prefix
        // against "/" + full-name.
        // The exact-match test
        // ("/copy" + Tab → null) falls
        // out naturally: fragment
        // length equals command
        // length so the "longer than"
        // guard skips it.
        if (fragment.Length <= 1)
        {
            // Just "/" alone or
            // empty — the user
            // hasn't typed any
            // command name yet.
            // We could surface the
            // full list here, but
            // for now a Tab on
            // bare "/" is a no-op
            // (the user types the
            // first letter and
            // Tabs again).
            return null;
        }
        foreach (var command in BuiltInSlashCommands)
        {
            if (command.Length > fragment.Length &&
                command.StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }
        }
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanStopTask))]
    private void StopTask()
    {
        _runCts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void RetryLastTask()
    {
        if (string.IsNullOrEmpty(_lastUserPrompt))
        {
            return;
        }

        DraftPrompt = _lastUserPrompt;
        _pendingRetriedRunId = _lastRunId;
        // Re-enter the send command. SendTaskCommand reads
        // DraftPrompt, so we don't pass the prompt in directly.
        if (SendTaskCommand.CanExecute(null))
        {
            SendTaskCommand.Execute(null);
        }
    }

    // 1.0.1: re-send the last user prompt
    // without the CanRetry gate. Where
    // RetryLastTask is the "I had a
    // failed run, try again" flow,
    // RegenerateLastResponse is the
    // "the last response wasn't bad,
    // I just want a different one"
    // flow. The two are different
    // daily-driver motions: retry
    // expects the same context to
    // keep failing; regenerate
    // expects a fresh take. The new
    // method appends a new AI
    // bubble (it doesn't replace the
    // old one — the conversation
    // history is the user's record
    // of what the model produced, not
    // a mutable draft).
    //
    // Returns true if a fresh send
    // was kicked off, false if there
    // was no prior prompt to re-send
    // (so the slash handler / AI
    // bubble button can report
    // "nothing to regenerate" rather
    // than silently no-op).
    public bool RegenerateLastResponse()
    {
        if (string.IsNullOrEmpty(_lastUserPrompt))
        {
            return false;
        }
        if (IsRunning)
        {
            return false;
        }
        DraftPrompt = _lastUserPrompt;
        if (SendTaskCommand.CanExecute(null))
        {
            SendTaskCommand.Execute(null);
            return true;
        }
        return false;
    }

    public void RecordLastRun(AgentRun run)
    {
        _lastRunId = run.Id;
    }

    public void PrepareContinuation(AgentRun run)
    {
        ClearPreparedRunLink();
        _pendingContinuationRunId = run.Id;
        _lastRunId = run.Id;
        _lastUserPrompt = run.Goal;
        DraftPrompt = "";
        _setStatusMessage("已载入历史运行。输入新的要求后发送，将沿用该运行的对话上下文。");
    }

    public void RetryRun(AgentRun run)
    {
        ClearPreparedRunLink();
        _pendingRetriedRunId = run.Id;
        _lastRunId = run.Id;
        _lastUserPrompt = run.Goal;
        DraftPrompt = run.Goal;
        _setStatusMessage("正在重试所选运行...");
        if (SendTaskCommand.CanExecute(null))
        {
            SendTaskCommand.Execute(null);
        }
    }

    public void ClearPreparedRunLink()
    {
        _pendingContinuationRunId = "";
        _pendingRetriedRunId = "";
    }

    public async Task ReportConversationPersistenceFailureAsync()
    {
        const string message = "运行结果仍保留在当前界面，但未能写入对话历史。请刷新后重试。";
        await _dispatchToUiAsync(() =>
        {
            _activityFeed.Add("对话保存失败", message, "警告");
            _toast.Show(message, ToastLevel.Warning);
        });
    }

    // Send is gated by both the agent-run state (IsRunning) and the
    // provider-test state. The previous shape only checked IsRunning;
    // a ⌘T test in flight wouldn't block a fresh send, so the user
    // could kick off a second agent run against a provider whose
    // first request hadn't returned yet. Now both gates must be
    // clear. TestStarted/TestCompleted flip the host's
    // IsProviderTesting through the Func<bool> bridge so the
    // underlying state stays on the host (where the events arrive).
    // 1.0.1: also gated on a non-empty
    // DraftPrompt so the send button is
    // visibly dim when the composer is
    // empty. SendTaskAsync's body still
    // has its own prompt-empty guard (the
    // toast "请先输入任务" path) so the
    // two layers don't drift — the XAML
    // dim is the discoverable signal,
    // the toast is the runtime safety
    // net for any caller that bypasses
    // the CanExecute gate. OnDraftPromptChanged
    // calls NotifyCanExecuteChanged
    // directly so the dim updates as the
    // user types / clears.
    private bool CanSendTask() =>
        !IsRunning && !IsVerifying && !_getIsProviderTesting()
        && !string.IsNullOrWhiteSpace(DraftPrompt);
    private bool CanStopTask() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanRunVerification))]
    private async Task RunVerificationAsync()
    {
        var project = _sidebar.CurrentProject;
        if (project is null || string.IsNullOrWhiteSpace(project.TryGetPrimaryPath()))
        {
            return;
        }

        IsVerifying = true;
        SendTaskCommand.NotifyCanExecuteChanged();
        _setStatusMessage($"正在运行 {project.VerificationCommands.Count} 项验证...");
        try
        {
            var results = await _verificationExecutor.RunAsync(project.TryGetPrimaryPath(), project.VerificationCommands);
            foreach (var result in results)
            {
                var detail = string.IsNullOrWhiteSpace(result.Summary)
                    ? result.Output
                    : $"{result.Command}\n{result.Summary}";
                _activityFeed.Add($"验证：{result.Name}", detail, result.IsSuccess ? "通过" : "失败");
            }

            var passed = results.Count(result => result.IsSuccess);
            _setStatusMessage($"验证完成：{passed}/{results.Count} 通过。");
            if (passed != results.Count)
            {
                _toast.Show("部分验证失败，请查看运行记录。", ToastLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            _setStatusMessage("验证执行失败。");
            _toast.Show(ex.Message, ToastLevel.Error);
        }
        finally
        {
            IsVerifying = false;
            SendTaskCommand.NotifyCanExecuteChanged();
        }
    }

    // The slash-command handler currently needs the host VM
    // (it reads /status-related fields off it). Until the slash
    // handler is also refactored to a smaller surface, we route
    // the call back through the host via a registered delegate.
    // Stored as a single Func so the call site stays readable;
    // the reference is injected in the host ctor.
    private Func<string, Task<(bool Handled, SlashCommandHandler.Result? Result)>>? _slashHandler;

    public void RegisterSlashHandler(Func<string, Task<(bool Handled, SlashCommandHandler.Result? Result)>> handler)
    {
        _slashHandler = handler;
    }

    private Task<(bool Handled, SlashCommandHandler.Result? Result)> GetHostForSlashHandler()
    {
        // No-op fallback: the slash command never fires when the
        // host hasn't registered a handler. The TryExecuteAsync
        // path returns Handled=false in that case so the agent
        // loop falls through. Avoids a NullReferenceException in
        // tests that don't wire the handler.
        if (_slashHandler is null)
        {
            return Task.FromResult((false, (SlashCommandHandler.Result?)null));
        }
        return _slashHandler(DraftPrompt);
    }

    // Convert every pending attachment (image or non-image) into a
    // project-level InputArtifact so the agent loop can attach the
    // file content to the next user message. Each artifact is
    // materialised via InputArtifactService (which classifies kind +
    // builds the summary) and persisted via InputArtifactFileStore
    // (which copies the file to the project's managed artifacts
    // folder and records storedPath in the metadata). The project
    // list is re-saved so the change is durable across restarts.
    //
    // Pending attachment rows are cleared at the end of the method
    // — the on-disk files have been copied to the project's
    // managed location, and the temporary files are removed when
    // the PendingAttachmentViewModel is disposed.
    //
    // MimeType + extension come from the PendingAttachmentViewModel
    // (filled in by AddPastedImage / AddFile) rather than being
    // hardcoded to image/png — the old shape was a paste-image-only
    // path; drag-and-drop introduced arbitrary file types and the
    // classifier downstream needs the real mime to dispatch to the
    // right text extractor (pdf / docx / xlsx / raw text).
    private async Task PromotePendingAttachmentsAsync(WorkspaceProject project)
    {
        if (PendingAttachments.Count == 0)
        {
            return;
        }

        var artifactService = new AIChat.Application.Artifacts.InputArtifactService();
        var snapshots = PendingAttachments.Attachments.ToList();

        foreach (var attachment in snapshots)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(attachment.FilePath);
                // Source label survives into the artifact metadata so
                // the agent can distinguish "user pasted a screenshot"
                // from "user dropped a 2MB PDF" when reasoning about
                // what to do with the file. The classifier still owns
                // the canonical Kind, so this is just provenance.
                var source = attachment.IsImage ? "pasted-image" : "dropped-file";
                var request = new AIChat.Application.Artifacts.InputArtifactCreateRequest
                {
                    ProjectId = project.Id,
                    ConversationId = "",
                    MessageId = "",
                    FileName = attachment.DisplayName,
                    MimeType = attachment.MimeType,
                    ContentText = "",
                    FileBytes = bytes,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["source"] = source,
                    },
                };
                var artifact = artifactService.Create(request);
                // Preserve the on-disk extension so the artifact
                // store's downstream consumers (file copy,
                // inspection tools, the user-facing file list) see
                // the original type. A pasted .png still becomes
                // .png; a dropped report.pdf stays .pdf.
                var extension = Path.GetExtension(attachment.DisplayName);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = Path.GetExtension(attachment.FileName);
                }
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".bin";
                }
                await _artifactFileStore.StoreBytesAsync(artifact, bytes, extension);
                project.InputArtifacts.Add(artifact);
            }
            catch (Exception ex)
            {
                // Single bad file shouldn't kill the whole send —
                // surface it as a system bubble and continue with
                // the rest.
                _activityFeed.Add(
                    "附加失败",
                    $"{attachment.DisplayName}: {ex.Message}",
                    "附件");
            }
        }

        // Persist the updated workspace (with the new artifacts) so
        // the change survives an app restart. Mirror the pattern
        // AgentRunnerViewModel uses after a run lands memory
        // updates. v1: 只写 workspaces,InputArtifacts 是 workspace 字段。
        var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
        var index = workspaces.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            workspaces[index] = project;
        }
        else
        {
            workspaces.Add(project);
        }
        await _repository.SaveWorkspacesAsync(workspaces);

        // Drop the UI rows (Dispose deletes the temp files; the
        // managed copies in the artifact store are now the source
        // of truth).
        PendingAttachments.Clear();
    }

    // Wave 7 (parity plan §7 Wave 7) third slice: @-references
    // in the composer's prompt get promoted to project-
    // level InputArtifacts the same way the pending-
    // attachment strip does. The user types something
    // like "@web:abc123 用一句话总结" and the agent
    // receives the original prompt (with the @-text
    // intact, so it can see what was referenced) PLUS a
    // separate InputArtifact carrying the page text.
    //
    // Why a separate artifact instead of inlining the
    // text into the prompt: the prompt stays short (the
    // token count is the user's "first-glance" reading),
    // and the artifact pipeline already has a clean
    // path for the system-prompt section the agent
    // reads via AgentRequestFactory.
    //
    // Per-reference failure isolation matches the
    // attachment path: a single bad reference doesn't
    // kill the send; we record a system bubble so the
    // user notices without the run being blocked.
    // 1.0.1: internal so headless tests can drive
    // the @-reference → InputArtifact pipeline
    // directly without firing the full agent loop.
    // Visibility wired via
    // [InternalsVisibleTo("AIChat.Tests")] on
    // AIChat.App.Avalonia.csproj (Wave 11 review
    // fix). The send path calls this from
    // SendTaskAsync; tests call it with a synthetic
    // prompt and project to lock down the four
    // observable contracts:
    //   - no references → no-op
    //   - 1 reference → 1 InputArtifact on the
    //     project, persisted to the repository
    //   - 2 references → 2 InputArtifacts, both
    //     persisted
    //   - per-reference failure isolation (a bad
    //     artifact doesn't kill the rest of the
    //     send)
    internal async Task PromoteSourceReferencesAsync(
        string prompt,
        WorkspaceProject project)
    {
        var references = SourceReferenceParser.Parse(prompt, _sourceRegistry.Sources);
        if (references.Count == 0)
        {
            return;
        }

        var artifactService = new AIChat.Application.Artifacts.InputArtifactService();
        foreach (var reference in references)
        {
            try
            {
                var source = reference.Source;
                // The Source.Content is already plain
                // text (the clipboard capture path
                // stores the clipboard text directly;
                // the web-fetch path stores the
                // HtmlToText-reduced output). We use
                // the same "kind" the InputArtifact
                // classifier uses so the agent loop
                // gets the right text extractor.
                var kind = ClassifySourceKind(source);
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = $"@reference:{source.Kind}",
                    ["kind"] = kind.ToString(),
                    ["capturedAt"] = source.CapturedAt.ToString("O"),
                };
                if (source.Metadata.TryGetValue("url", out var url))
                {
                    metadata["url"] = url;
                }
                var request = new AIChat.Application.Artifacts.InputArtifactCreateRequest
                {
                    ProjectId = project.Id,
                    ConversationId = "",
                    MessageId = "",
                    FileName = source.DisplayName,
                    MimeType = source.MimeTypeOrFallback(kind),
                    ContentText = source.Content,
                    FileBytes = System.Text.Encoding.UTF8.GetBytes(source.Content),
                    Metadata = metadata,
                };
                var artifact = artifactService.Create(request);
                await _artifactFileStore.StoreBytesAsync(artifact, request.FileBytes, ".txt");
                project.InputArtifacts.Add(artifact);
            }
            catch (Exception ex)
            {
                _activityFeed.Add(
                    "引用失败",
                    $"{reference.Source.DisplayName}: {ex.Message}",
                    "附件");
            }
        }

        // Persist the updated workspace so the
        // @-referenced artifacts survive an app
        // restart. Same shape as the pending-
        // attachments path.
        var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
        var index = workspaces.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            workspaces[index] = project;
        }
        else
        {
            workspaces.Add(project);
        }
        await _repository.SaveWorkspacesAsync(workspaces);
    }

    // Map a Source's Kind to the InputArtifactKind the
    // classifier would have assigned. The Source
    // registry's free-form Kind string ('web' /
    // 'clipboard' / future 'connector') maps cleanly to
    // the same enum the paste-image path uses, so the
    // agent loop's existing text-extraction code can
    // pick the right reader without a new branch.
    private static AIChat.Domain.Artifacts.InputArtifactKind ClassifySourceKind(
        AIChat.Domain.Sources.Source source)
    {
        return source.Kind.ToLowerInvariant() switch
        {
            "web" or "webpage" => AIChat.Domain.Artifacts.InputArtifactKind.Document,
            "clipboard" => AIChat.Domain.Artifacts.InputArtifactKind.Text,
            "image" => AIChat.Domain.Artifacts.InputArtifactKind.Image,
            _ => AIChat.Domain.Artifacts.InputArtifactKind.Text,
        };
    }

    // Rebuild PlanItems from the current AgentPlan. Called by the
    // AgentRunner on every StepAdded / SubAgentStarted /
    // SubAgentCompleted event (so the plan list stays in lockstep
    // with what the agent just wrote). Items appear in the same
    // order the agent wrote them.
    public void UpdatePlan(AgentPlan? plan)
    {
        // Capture which rows the user had expanded before the
        // rebuild. The runner emits a full plan on every step
        // (a "snapshot replace" model), so PlanItems.Clear() is
        // a normal occurrence — without the snapshot the user's
        // "step 3 — read X" expansion would collapse the moment
        // the agent adds a new step 4, and the daily-driver user
        // would have to re-click to keep reading the notes.
        // AgentPlanItem.Id is a stable GUID the runner
        // preserves across updates, so the set round-trips.
        var previouslyExpanded = PlanItems
            .Where(item => item.IsExpanded && !string.IsNullOrEmpty(item.Id))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        PlanItems.Clear();
        if (plan is null)
        {
            // The XAML's plan panel binds IsVisible to HasPlan —
            // raise it here too, otherwise the panel never
            // collapses back hidden after the agent's last run
            // finishes and clears the plan. (Same reason for the
            // two other derived properties: their PropertyChanged
            // only fires when the observable source flips, and
            // PlanItems.Clear() / Add() are collection events, not
            // per-property notifications.)
            OnPropertyChanged(nameof(HasPlan));
            OnPropertyChanged(nameof(PlanCompletedCount));
            OnPropertyChanged(nameof(PlanProgressText));
            return;
        }
        foreach (var item in plan.Items.OrderBy(item => item.Order))
        {
            PlanItems.Add(new PlanItemViewModel
            {
                // Carry the domain id so the next rebuild
                // can match this row back to a previous
                // expansion. Without this, the
                // previouslyExpanded set below would be
                // empty after the first refresh and every
                // later expand would collapse on the next
                // step.
                Id = item.Id,
                Title = item.Title,
                // 1.0.1: the runner / planner
                // can attach tool names,
                // file paths, and rationale
                // to a step via Notes. The
                // XAML row hides the
                // expand affordance when
                // Notes is empty so the
                // clickable area doesn't
                // pretend there's more
                // to see.
                Notes = item.Notes,
                Status = item.Status,
                // Restore the user's previous expand
                // state. New rows (the agent just added
                // step 5) start collapsed; existing rows
                // the user had open stay open.
                IsExpanded = previouslyExpanded.Contains(item.Id)
            });
        }
        // HasPlan is the IsVisible for the whole plan panel —
        // without raising it the panel stays hidden the whole
        // session because PlanItems.Add() is a collection event,
        // not a "HasPlan" PropertyChanged. The XAML only
        // re-evaluates IsVisible on "HasPlan" notifications.
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanCompletedCount));
        OnPropertyChanged(nameof(PlanProgressText));
    }

    // 1.0.1: long plans (10+ items, the
    // common case for refactor /
    // migration runs) are painful to
    // expand one row at a time when
    // the user wants to read the
    // whole plan up front. The two
    // header buttons (全部展开 / 全部
    // 折叠) call these to flip every
    // row's IsExpanded in one shot.
    // PlanItemViewModel.IsExpanded
    // raises PropertyChanged so the
    // XAML inline detail panel
    // toggles immediately.
    public void ExpandAllPlanItems()
    {
        foreach (var item in PlanItems)
        {
            item.IsExpanded = true;
        }
    }

    public void CollapseAllPlanItems()
    {
        foreach (var item in PlanItems)
        {
            item.IsExpanded = false;
        }
    }

    public void UpsertSubAgentRun(AgentSubAgentRun run)
    {
        // Match by id so the same sub-agent row updates in place
        // when the harness emits started (status=Running,
        // duration=空) then completed (status=Completed,
        // duration=12s). The plan panel's sub-section reads
        // status + duration off the row.
        var existing = SubAgentRuns.FirstOrDefault(item => item.Id == run.Id);
        if (existing is not null)
        {
            existing.Update(run);
        }
        else
        {
            var card = new SubAgentRunViewModel(run);
            // 2026-08-03: wire the per-row stop button to this
            // host's CancelSubAgent. The relay-command indirection
            // keeps the row's view-model free of the
            // SubAgentScheduler dependency; the host owns the
            // lifecycle and the row only knows its own id.
            card.StopCommand = new RelayCommand<string?>(
                id => { if (!string.IsNullOrEmpty(id)) CancelSubAgent(id); },
                id => id == run.Id);
            SubAgentRuns.Add(card);
        }
        // HasSubAgentRuns is the IsVisible binding for the
        // sub-section. Collection mutations don't fire
        // PropertyChanged on a derived bool, so re-raise here.
        OnPropertyChanged(nameof(HasSubAgentRuns));
    }

    public void ClearSubAgentRuns()
    {
        SubAgentRuns.Clear();
        // Re-raise so the sub-section collapses back to hidden
        // when a new SendTaskCommand starts. Without this the
        // IsVisible binding stays at its last-true value until
        // the next sub-agent event lands and UpsertSubAgentRun
        // fires its own re-raise.
        OnPropertyChanged(nameof(HasSubAgentRuns));
    }

    // 1.0.1: clear every in-flight run-state
    // surface when the user switches to a
    // different conversation. The previous
    // shape only cleared ActivityFeed on
    // conversation switch — PlanItems /
    // SubAgentRuns / LastAssistantStatus
    // / InputTokens / IsRunning stayed at
    // their last-true values, so a daily-
    // driver user who finished a run in
    // conversation A, then clicked
    // conversation B, saw conversation A's
    // plan steps + sub-agent rows still
    // rendered above the (correctly
    // swapped) activity feed. The plan
    // panel is bound to AgentHost.PlanItems
    // (not the per-conversation activity
    // feed), so the previous
    // conversation's plan leaked across.
    //
    // IsRunning is the one field this
    // method does NOT clear: if a run is
    // genuinely in flight (the user
    // started a long task and clicked a
    // different conversation while the
    // agent was still running), the host
    // stays running. StopTaskCommand is
    // what ends a run. Forcing
    // IsRunning=false here would race
    // the actual run continuation and
    // leave the next conversation's
    // composer in a stuck "can't send"
    // state.
    public void ClearRunState()
    {
        PlanItems.Clear();
        ClearSubAgentRuns();
        // Reset the per-conversation status
        // fields the status bar / retry
        // button bind to. ContextInputTokens
        // recomputes on the next Recompute
        // (triggered by the next draft-prompt
        // change or sidebar project switch).
        LastAssistantStatus = "";
        InputTokens = 0;
        // Re-raise the derived bools the XAML
        // panels bind to so the plan / sub-
        // agent sections collapse back to
        // hidden, and the status bar's
        // "已完成" / "失败" pill clears.
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanCompletedCount));
        OnPropertyChanged(nameof(PlanProgressText));
        OnPropertyChanged(nameof(CanRetry));
    }

    // 2026-08-03: cancel an in-flight sub-agent. The user clicks
    // the per-row '停止' button; this walks up through the
    // scheduler (which holds the per-run CTS), and the next
    // event-loop turn re-renders the row with status = Cancelled.
    // The sub-agent finishes within one or two agent events
    // (the cancel propagates through AgentRunner to the LLM
    // stream, which then emits a terminal Cancelled event).
    public void CancelSubAgent(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }
        try
        {
            _subAgentScheduler?.CancelAsync(runId);
        }
        catch
        {
            // Cancellation is best-effort; the user can re-click
            // if the run is still running.
        }
    }

    // 2026-08-03: set by the host after construction so
    // CancelSubAgent has a target. Direct DI of the scheduler
    // into the view-model is fine, but the host's
    // CompositionRoot already has the instance and a setter
    // is the smallest change. ServiceRegistration wires this
    // up at AppHost.Build() time.
    public AIChat.Application.Agents.SubAgents.SubAgentScheduler? SubAgentScheduler
    {
        get => _subAgentScheduler;
        set => _subAgentScheduler = value;
    }
    private AIChat.Application.Agents.SubAgents.SubAgentScheduler? _subAgentScheduler;

    // Re-runs the context router for the current project + goal
    // and updates InputTokens. The only consumer is the
    // status-bar context meter, so this method is called on every
    // event that could shift the estimate: project selection,
    // prompt keystrokes, and no-write toggle. Cheap because the
    // router + file-index builder cache internally; running on
    // every keystroke is fine.
    //
    // Every caller fires this without awaiting (it's a
    // status-bar polish, not a hard dependency). Two hardening
    // layers:
    //   1. 200ms debounce. A rapid stream of keystrokes / project
    //      selection changes collapses to one recompute; the
    //      previous in-flight call gets cancelled at the next
    //      await point instead of all 7 racers writing the meter
    //      in arbitrary order.
    //   2. Outer try/catch around the whole body. The inner
    //      try/catch (built pre-PR) swallows exceptions from the
    //      Task.Run paths; this outer one also catches anything
    //      that escapes the project-read / debounce-delay paths
    //      and was the second async-void crash chain that the
    //      847a598 fix taught us to avoid. Swallow + surface via
    //      StatusMessage so the user sees what happened and the
    //      meter just stops updating for the rest of the session.
    public async Task RecomputeContextInputTokensAsync(string goal)
    {
        CancellationToken token;
        lock (_recomputeLock)
        {
            _recomputeCts?.Cancel();
            _recomputeCts = new CancellationTokenSource();
            token = _recomputeCts.Token;
        }

        try
        {
            // Debounce: 200ms idle window so a rapid stream of
            // keystrokes collapses to one recompute. The 7
            // callers all fire-and-forget so the delay is
            // invisible to the user; what they see is a stable
            // meter that updates ~200ms after they stop typing.
            await Task.Delay(TimeSpan.FromMilliseconds(200), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var project = _sidebar.CurrentProject;
            if (project is null || string.IsNullOrWhiteSpace(project.TryGetPrimaryPath()) || !Directory.Exists(project.TryGetPrimaryPath()))
            {
                InputTokens = 0;
                return;
            }

            var resolvedGoal = string.IsNullOrWhiteSpace(goal) ? "项目概览" : goal.Trim();
            var fileIndex = await Task.Run(() => new ProjectFileIndexBuilder().Build(project.TryGetPrimaryPath(), maxFiles: 500), token);
            var contextPack = await Task.Run(() => new ContextRouter().Route(new ContextRouterRequest
            {
                Goal = resolvedGoal,
                Phase = AgentRunPhase.GatheringContext,
                FileIndex = fileIndex,
                PinnedItems = project.PinnedContext,
                InputArtifacts = project.InputArtifacts,
                MemorySnippets = project.Memories.Select(memory => memory.Content).ToList(),
                MaxTokens = 900
            }), token);
            InputTokens = ContextInputEstimator.Estimate(contextPack.EstimatedTokens, resolvedGoal);
        }
        catch (OperationCanceledException)
        {
            // A newer recompute superseded us. Silent exit; the
            // newer call will publish its own result.
        }
        catch (Exception ex)
        {
            _setStatusMessage($"Context 估算失败：{ex.Message}");
        }
    }
}
