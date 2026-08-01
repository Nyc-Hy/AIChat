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
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
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
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly ConversationListViewModel _conversationList;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTaskCommand))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string lastAssistantStatus = "";

    public bool CanRetry =>
        !string.IsNullOrEmpty(_lastUserPrompt)
        && !IsRunning
        && (LastAssistantStatus is "失败" or "已停止");

    [ObservableProperty]
    private string draftPrompt = "";

    // Approximate context window. 64K covers GPT-4 / Claude /
    // DeepSeek with a single number so the input-area progress bar
    // reads consistently. Will become per-model once the provider
    // API reports the real cap.
    private const int ApproximateContextWindow = 64_000;

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
    private int inputTokens;

    public int ContextBudgetPercent =>
        (int)Math.Clamp(InputTokens * 100.0 / ApproximateContextWindow, 0, 100);

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
        Action<string> setStatusMessage,
        Func<AppSettings> getSettings,
        Func<bool> getNoWriteMode,
        Func<bool> getIsProviderTesting)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _approval = approval;
        _repository = repository;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _activityFeed = activityFeed;
        _toast = toast;
        _setStatusMessage = setStatusMessage;
        _getSettings = getSettings;
        _getNoWriteMode = getNoWriteMode;
        _getIsProviderTesting = getIsProviderTesting;

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
    }

    private void OnProjectAdded(object? sender, ProjectAddedEventArgs args)
    {
        _ = RecomputeContextInputTokensAsync(DraftPrompt);
    }

    partial void OnDraftPromptChanged(string value)
    {
        _ = RecomputeContextInputTokensAsync(value);
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
            var projectRoot = _sidebar.CurrentProject?.Path;
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

            if (_sidebar.CurrentProject is null || string.IsNullOrWhiteSpace(_sidebar.CurrentProject.Path))
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
                await _agentRunner.RunAsync(prompt, effectiveSettings, _runCts.Token);
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
        // Re-enter the send command. SendTaskCommand reads
        // DraftPrompt, so we don't pass the prompt in directly.
        if (SendTaskCommand.CanExecute(null))
        {
            SendTaskCommand.Execute(null);
        }
    }

    // Send is gated by both the agent-run state (IsRunning) and the
    // provider-test state. The previous shape only checked IsRunning;
    // a ⌘T test in flight wouldn't block a fresh send, so the user
    // could kick off a second agent run against a provider whose
    // first request hadn't returned yet. Now both gates must be
    // clear. TestStarted/TestCompleted flip the host's
    // IsProviderTesting through the Func<bool> bridge so the
    // underlying state stays on the host (where the events arrive).
    private bool CanSendTask() => !IsRunning && !_getIsProviderTesting();
    private bool CanStopTask() => IsRunning;

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

    // Convert every pending image attachment into a project-level
    // InputArtifact so the agent loop can attach the image content
    // to the next user message. Each artifact is materialised via
    // InputArtifactService (which classifies kind + builds the
    // summary) and persisted via InputArtifactFileStore (which
    // copies the file to the project's managed artifacts folder
    // and records storedPath in the metadata). The project list
    // is re-saved so the change is durable across restarts.
    //
    // Pending attachment rows are cleared at the end of the method
    // — the on-disk files have been copied to the project's
    // managed location, and the temporary files are removed when
    // the PendingAttachmentViewModel is disposed.
    private async Task PromotePendingAttachmentsAsync(ProjectWorkspace project)
    {
        if (PendingAttachments.Count == 0)
        {
            return;
        }

        var artifactService = new AIChat.Application.Artifacts.InputArtifactService();
        var fileStore = new AIChat.Application.Artifacts.InputArtifactFileStore();
        var snapshots = PendingAttachments.Attachments.ToList();

        foreach (var attachment in snapshots)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(attachment.FilePath);
                var request = new AIChat.Application.Artifacts.InputArtifactCreateRequest
                {
                    ProjectId = project.Id,
                    ConversationId = "",
                    MessageId = "",
                    FileName = attachment.FileName,
                    MimeType = "image/png",
                    ContentText = "",
                    FileBytes = bytes,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["source"] = "pasted-image",
                    },
                };
                var artifact = artifactService.Create(request);
                await fileStore.StoreBytesAsync(artifact, bytes, ".png");
                project.InputArtifacts.Add(artifact);
            }
            catch (Exception ex)
            {
                // Single bad file shouldn't kill the whole send —
                // surface it as a system bubble and continue with
                // the rest.
                _activityFeed.Add(
                    "附加失败",
                    $"{attachment.FileName}: {ex.Message}",
                    "附件");
            }
        }

        // Persist the updated project (with the new artifacts) so
        // the change survives an app restart. Mirror the pattern
        // AgentRunnerViewModel uses after a run lands memory
        // updates.
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var index = projects.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            projects[index] = project;
        }
        else
        {
            projects.Add(project);
        }
        await _repository.SaveProjectsAsync(projects);

        // Drop the UI rows (Dispose deletes the temp files; the
        // managed copies in the artifact store are now the source
        // of truth).
        PendingAttachments.Clear();
    }

    // Rebuild PlanItems from the current AgentPlan. Called by the
    // AgentRunner on every StepAdded / SubAgentStarted /
    // SubAgentCompleted event (so the plan list stays in lockstep
    // with what the agent just wrote). Items appear in the same
    // order the agent wrote them.
    public void UpdatePlan(AgentPlan? plan)
    {
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
                Title = item.Title,
                Status = item.Status
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
            SubAgentRuns.Add(new SubAgentRunViewModel(run));
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
            if (project is null || string.IsNullOrWhiteSpace(project.Path) || !Directory.Exists(project.Path))
            {
                InputTokens = 0;
                return;
            }

            var resolvedGoal = string.IsNullOrWhiteSpace(goal) ? "项目概览" : goal.Trim();
            var fileIndex = await Task.Run(() => new ProjectFileIndexBuilder().Build(project.Path, maxFiles: 500), token);
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
