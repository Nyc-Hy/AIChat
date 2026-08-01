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

public sealed partial class MainWindowViewModel : ViewModelBase, ISlashCommandHost
{
    private readonly IAppRepository _repository;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IChatCompletionService _chatService;
    private readonly ProviderConfigViewModel _provider;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ConversationListViewModel _conversationList;
    private readonly ToolApprovalViewModel _approvalViewModel;
    private readonly IThemeService _theme;
    private readonly ISettingsHolder _settingsHolder;
    private readonly IToastService _toast;
    private readonly IProjectPicker _projectPicker;
    private readonly IClipboardService _clipboard;
    private readonly MemoryEditorViewModel _memoryEditor;
    private readonly GitStatusViewModel _gitStatus;
    private readonly AIChat.Application.Workspace.IWorkspaceChangeService _workspace;
    private readonly AgentHostViewModel _agentHost;

    // lastAssistantStatus + CanRetry + _lastUserPrompt all moved
    // to AgentHostViewModel — see the AgentHost property. The host
    // doesn't need a local mirror; XAML binds to
    // AgentHost.CanRetry / AgentHost.LastAssistantStatus.

    private AppSettings _settings = new();

    [ObservableProperty]
    private string activeProvider = "正在加载...";

    [ObservableProperty]
    private string activeModel = "";

    [ObservableProperty]
    private string readiness = "检查中";

    // True while a connection test (⌘T / "测试当前模型") is in
    // flight. Set by OnProviderTestStarted/Completed. Read by the
    // agent host through a Func<bool> bridge so CanSendTask can
    // disable the send button — otherwise a test in flight would
    // race a freshly-sent agent run against the same provider.
    // Lives on the host (not the agent host) because the test is
    // triggered from ProviderConfigViewModel; the agent host never
    // mutates it.
    [ObservableProperty]
    private bool isProviderTesting;

    // OnIsProviderTestingChanged fires whenever the test-start /
    // test-complete event pair flips the gate. Re-evaluate the
    // agent host's send / stop commands so the send button
    // disables mid-test and re-enables the moment the test
    // completes. Can't use [NotifyCanExecuteChangedFor] here
    // because the commands live on AgentHost, not on the host.
    partial void OnIsProviderTestingChanged(bool value)
    {
        _agentHost.SendTaskCommand.NotifyCanExecuteChanged();
        _agentHost.StopTaskCommand.NotifyCanExecuteChanged();
    }

    // Computed view-state properties derive from the observables above. Avalonia
    // bindings do not pick up changes to plain CLR properties; we re-raise
    // PropertyChanged manually so the breadcrumb / greeting / status bar update
    // when the underlying fields flip.
    partial void OnActiveProviderChanged(string value)
    {
        OnPropertyChanged(nameof(StatusBarModel));
    }
    partial void OnActiveModelChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnReadinessChanged(string value)
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(NeedsConfiguration));
    }

    [ObservableProperty]
    private string draftPrompt = "";

    [ObservableProperty]
    private string statusMessage = "就绪。";

    [ObservableProperty]
    private bool noWriteMode;

    // Placeholder text for the prompt TextBox. Changes when read-only
    // mode is toggled so the user always knows whether their next
    // message can mutate the project. ⌘⇧R toggles the mode (and
    // therefore the placeholder).
    public string PromptPlaceholder => NoWriteMode
        ? "只读模式 — 探索 / 提问，不修改项目 (⌘⇧R 切换)"
        : "说点什么…  (试试 /help 查看命令)";

    partial void OnNoWriteModeChanged(bool value)
    {
        _approvalViewModel.IsReadOnly = value;
        // The no-write toggle shifts which tools the agent can see,
        // which shifts the system prompt size, which shifts the
        // context estimate — recompute the meter on toggle. Host
        // owns NoWriteMode; AgentHost owns the recompute.
        _ = _agentHost.RecomputeContextInputTokensAsync(_agentHost.DraftPrompt);
        OnPropertyChanged(nameof(PromptPlaceholder));
    }

    // Clipboard helpers used by the /copy slash command. HasClipboardService
    // lets the slash handler fail gracefully when the platform clipboard
    // isn't wired (e.g. during tests where no TopLevel has been set).
    public bool HasClipboardService => _clipboard.IsAvailable;

    public Task CopyToClipboardAsync(string text) => _clipboard.SetTextAsync(text);

    // Git status helper used by the /git-status slash command. Renders
    // the current project's branch + a compact change list as a single
    // string the host can drop into the activity feed. The full
    // WorkspaceChangeService handles the underlying git call; this
    // method is the presentation layer.
    public async Task<string> GetGitStatusSummaryAsync()
    {
        var project = _sidebar.CurrentProject;
        if (project is null || string.IsNullOrWhiteSpace(project.Path))
        {
            return "(请先选择项目)";
        }

        AIChat.Application.Workspace.WorkspaceChangeSet changeSet;
        try
        {
            changeSet = await _workspace.GetChangesAsync(project.Path);
        }
        catch (Exception ex)
        {
            return $"git 状态读取失败：{ex.Message}";
        }

        var branch = string.IsNullOrWhiteSpace(changeSet.Branch)
            ? "(无分支信息)"
            : changeSet.Branch.TrimStart('#', ' ');
        if (changeSet.Changes.Count == 0)
        {
            return $"分支: {branch}\n工作区干净，没有未提交改动。";
        }

        var lines = new List<string>
        {
            $"分支: {branch}",
            $"{changeSet.Changes.Count} 个变更文件:",
            "",
        };
        foreach (var change in changeSet.Changes)
        {
            var tag = change.IsUntracked ? "未跟踪" : change.DisplayStatus;
            lines.Add($"  [{tag}] {change.Path}");
        }
        if (changeSet.IsTruncated)
        {
            lines.Add("");
            lines.Add("  … 已截断。完整列表请在终端运行 git status。");
        }
        return string.Join("\n", lines);
    }

    // AppSettings schema mirrors (Temperature, MaxOutputTokens,
    // RetryMaxAttempts, UseTokenizerEstimation, MaxAutoFixRounds,
    // AgentExecutionMode, AutoVerify, Tools permission matrix) live in
    // SettingsViewModel now — see the Settings property above. The host
    // no longer carries these fields. XAML binds to Settings.X for each.

    // The run state (IsRunning, LastAssistantStatus, InputTokens,
    // DraftPrompt, PendingAttachments, PlanItems, SubAgentRuns) and
    // the send / stop / retry commands live in AgentHostViewModel —
    // see the AgentHost property. The host keeps the cross-cutting
    // concerns (sidebar / conversation wiring, approval bubbles,
    // modals, settings surface) and reads / writes a small
    // Action/Func bridge for the host-owned state the agent runner
    // touches (StatusMessage, AppSettings, NoWriteMode).

    // 1.0 Beta: command palette + settings modal overlays. The toggles flip
    // a Border's IsVisible in the MainWindow XAML.
    [ObservableProperty]
    private bool isCommandPaletteOpen;

    [ObservableProperty]
    private bool isSettingsOpen;

    // Memory editor modal: full add / delete UI for the current
    // project's memory. ⌘⇧M opens it. /memory (slash) stays as a
    // quick read-only summary in the activity feed — this is the
    // edit surface.
    [ObservableProperty]
    private bool isMemoryEditorOpen;

    public MemoryEditorViewModel MemoryEditor => _memoryEditor;

    // Git status / diff viewer modal. ⌘⇧G opens it; ⌘G stays as the
    // quick /git bubble for the lightweight "what just changed"
    // glance.
    [ObservableProperty]
    private bool isGitStatusOpen;

    public GitStatusViewModel GitStatus => _gitStatus;

    // 1.0 Beta: derive the top status, breadcrumb visibility and status-bar
    // text from the same handful of fields so the XAML can stay declarative.
    // HasProject hides the project crumb when no project is selected (so
    // the breadcrumb doesn't read "AIChat / 未配置路径"). IsReady /
    // NeedsConfiguration drive the compact status pills.
    public bool HasProject => !string.IsNullOrWhiteSpace(Sidebar.SelectedProjectName)
                              && Sidebar.SelectedProjectName != "未配置路径";

    public bool IsReady => Readiness == "可运行";
    public bool NeedsConfiguration => Readiness == "需要密钥" || Readiness == "需检查";

    public string Greeting => HasProject ? "今天要完成什么？" : "选一个项目开始";
    public string SubGreeting => HasProject
        ? "输入目标后，AIChat 会读取项目上下文并在风险操作前询问你。"
        : "添加本地代码仓库，让 AIChat 读取上下文后再开始任务。";

    public string StatusBarModel => string.IsNullOrEmpty(ActiveModel)
        ? ActiveProvider
        : $"{ActiveProvider} · {ActiveModel}";

    // Approximate context window + estimated input tokens + the
    // status-bar context meter all moved to AgentHostViewModel
    // (it's the run state). The XAML binds through AgentHost.

    // PR-12: conversation activity feed is its own view-model. The XAML
    // binds to ActivityFeed.Activity / ActivityFeed.HasConversation.
    public ActivityFeedViewModel ActivityFeed { get; } = new();

    // Toast surface is owned by the IToastService singleton; expose the same
    // ObservableCollection here so the MainWindow XAML can bind via DataContext.
    public ObservableCollection<ToastItem> Toasts => _toast.Toasts;

    // Command palette is its own view-model; the MainWindow binds a search box
    // and an ItemsControl to it.
    public CommandPaletteViewModel CommandPalette { get; } = new();

    // 1.0 Beta: top-level commands the MainWindow code-behind binds to
    // keyboard shortcuts (Cmd+K, Cmd+,). Both flip a single bool so the
    // XAML only has to react to one property change.
    [RelayCommand]
    private void OpenCommandPalette()
    {
        // v1 bug B-2 fix: reset palette state on every open. Previously the
        // second open inherited the previous search text and selected index
        // because the palette's own IsOpen was never written (only this
        // VM's IsCommandPaletteOpen was), so the OnIsOpenChanged partial
        // hook in CommandPaletteViewModel never ran.
        CommandPalette.SearchText = "";
        CommandPalette.SelectedIndex = 0;
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void OpenMemoryEditor()
    {
        // Refresh the list every time the modal opens so a memory
        // added by the agent during a run is reflected immediately.
        _memoryEditor.Refresh();
        IsMemoryEditorOpen = true;
    }

    [RelayCommand]
    private void CloseMemoryEditor() => IsMemoryEditorOpen = false;

    [RelayCommand]
    public async Task OpenGitStatusAsync()
    {
        IsGitStatusOpen = true;
        // Re-fetch every open so an agent run that just landed
        // shows up immediately. Cheap (single git status call) and
        // the user opened the modal because they want to see what's
        // there right now.
        await _gitStatus.RefreshAsync();
    }

    [RelayCommand]
    private void CloseGitStatus() => IsGitStatusOpen = false;

    [RelayCommand]
    private void NewConversation()
    {
        ActivityFeed.Clear();
        StatusMessage = "新对话。";
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.CycleToNext();

    // PR-2: provider config surface is delegated to a dedicated view-model.
    public ProviderConfigViewModel Provider => _provider;

    // 1.0 refactor: AppSettings schema mirrors (Temperature, MaxOutputTokens,
    // AgentExecutionMode, AutoVerify, tool permission matrix) live in a
    // dedicated view-model. The host keeps the cross-cutting concerns
    // (project + conversation + activity + run state) and exposes Settings
    // as a sub-VM the XAML can bind to. Schema writes go through
    // SettingsViewModel.OnXxxChanged partials, which fire-and-forget save
    // via the shared IAppRepository.
    public SettingsViewModel Settings => _settingsViewModel;

    // 1.0 refactor: agent run state (SendTask / StopTask / RetryLastTask,
    // IsRunning, LastAssistantStatus, InputTokens, DraftPrompt,
    // PendingAttachments, PlanItems, SubAgentRuns) lives in a dedicated
    // sub-VM. The host keeps the cross-cutting glue (sidebar / conversation
    // wiring, approval bubbles, modals, settings surface) and exposes
    // AgentHost for XAML binding. The host bridges the three pieces of
    // shared state (StatusMessage, AppSettings, NoWriteMode) into AgentHost
    // through a small Action/Func bridge.
    public AgentHostViewModel AgentHost => _agentHost;

    // PR-3: project list / selection lives in a dedicated view-model. The
    // current project is exposed as a public property (CurrentProject) so
    // the rest of the app can read it without going through events.
    public ProjectSidebarViewModel Sidebar => _sidebar;

    // PR-4: recent conversations list and selection live in a dedicated
    // view-model. The activity feed still belongs here; the parent reacts
    // to ConversationSelected events to load messages.
    public ConversationListViewModel ConversationList => _conversationList;

    // PR-6: tool approval dialog and Approve / Reject commands live in a
    // dedicated view-model. The IApprovalService is what the agent
    // harness depends on; the service is a thin facade over the VM.
    public ToolApprovalViewModel Approval => _approvalViewModel;

    // 1.0 refactor: the inner agent loop (harness, event streaming,
    // conversation persistence) and the run state (SendTask /
    // StopTask / RetryLastTask, IsRunning, LastAssistantStatus,
    // InputTokens, DraftPrompt, PendingAttachments, PlanItems,
    // SubAgentRuns) all live in AgentHostViewModel. The host
    // exposes AgentHost for XAML binding and feeds the host-owned
    // state (StatusMessage, AppSettings, NoWriteMode) into it
    // through a small bridge.

    // Count of new activity bubbles that landed while the user was
    // scrolled up reading history. The conversation view only
    // auto-scrolls to the bottom when the user is at the bottom; this
    // counter is what the floating "↓ N 条新消息" pill shows so the
    // user knows there's new content waiting. Reset to 0 when they
    // scroll back to the bottom or click the pill.
    // Scroll-state for the conversation panel. Extracted into a
    // sub-VM in the v1.0 refactor so the host doesn't carry the
    // counter, derived labels, and the bump / clear methods the
    // auto-scroll handler pushes into. XAML still binds through
    // MainWindowViewModel.MessageScroll.{HasUnseenMessages,
    // UnseenMessageLabel} for now — the two paths go through the
    // sub-VM's PropertyChanged which bubbles through the host's
    // own PropertyChanged. (Re-binding directly to MessageScroll
    // would be the next step but requires touching XAML; out of
    // scope for this commit.)
    public MessageScrollState MessageScroll { get; } = new();

    public bool HasUnseenMessages => MessageScroll.HasUnseenMessages;
    public string UnseenMessageLabel => MessageScroll.UnseenMessageLabel;
    public void IncrementUnseenMessageCount() => MessageScroll.IncrementUnseenMessageCount();
    public void ClearUnseenMessageCount() => MessageScroll.ClearUnseenMessageCount();

    public MainWindowViewModel(
        IAppRepository repository,
        AgentToolRegistry toolRegistry,
        IChatCompletionService chatService,
        ProviderConfigViewModel provider,
        SettingsViewModel settingsViewModel,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        ToolApprovalViewModel approvalViewModel,
        IApprovalService approval,
        IThemeService theme,
        ISettingsHolder settingsHolder,
        IToastService toast,
        IProjectPicker projectPicker,
        IClipboardService clipboard,
        MemoryEditorViewModel memoryEditor,
        GitStatusViewModel gitStatus,
        AIChat.Application.Workspace.IWorkspaceChangeService workspace)
    {
        _repository = repository;
        _toolRegistry = toolRegistry;
        _chatService = chatService;
        _provider = provider;
        _settingsViewModel = settingsViewModel;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _approvalViewModel = approvalViewModel;
        _theme = theme;
        _settingsHolder = settingsHolder;
        _toast = toast;
        _projectPicker = projectPicker;
        _clipboard = clipboard;
        _memoryEditor = memoryEditor;
        _gitStatus = gitStatus;
        _workspace = workspace;

        // Construct the agent host (which in turn owns the
        // AgentRunnerViewModel + the per-run CTS + the run state).
        // The bridge delegates (setStatusMessage, getSettings,
        // getNoWriteMode) are the only host-owned state the runner
        // touches — everything else the runner writes to lives
        // inside AgentHost.
        _agentHost = new AgentHostViewModel(
            chatService,
            toolRegistry,
            approval,
            repository,
            sidebar,
            conversationList,
            ActivityFeed,
            toast,
            setStatusMessage: value => StatusMessage = value,
            getSettings: () => _settings,
            getNoWriteMode: () => NoWriteMode,
            getIsProviderTesting: () => IsProviderTesting);

        // The slash-command handler is a small static helper that
        // currently expects the host VM (it reads /status fields off
        // it). Until the slash handler is also refactored to a
        // smaller surface, the host routes the call through a
        // single delegate. The reference lives on AgentHost so
        // SendTaskAsync's call site stays readable.
        _agentHost.RegisterSlashHandler(prompt =>
            SlashCommandHandler.TryExecuteAsync(prompt, this));

        _provider.Saved += OnProviderSaved;
        _provider.TestStarted += OnProviderTestStarted;
        _provider.TestCompleted += OnProviderTestCompleted;
        _sidebar.ProjectSelected += OnSidebarProjectSelected;
        _sidebar.ProjectAdded += OnSidebarProjectAdded;
        _conversationList.ConversationSelected += OnConversationSelected;
        _approvalViewModel.RequestPresented += OnApprovalPresented;
        _approvalViewModel.RequestResolved += OnApprovalResolved;

        _sidebar.PropertyChanged += (_, e) =>
        {
            // SelectedProjectName drives HasProject, Greeting and SubGreeting;
            // re-raise them so the breadcrumb / page title update.
            if (e.PropertyName == nameof(ProjectSidebarViewModel.SelectedProjectName))
            {
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(Greeting));
                OnPropertyChanged(nameof(SubGreeting));
            }
        };
        RegisterCommandPaletteCommands();

        _ = RefreshAsync();
    }

    private void OnSidebarProjectSelected(object? sender, ProjectSelectionChangedEventArgs args)
    {
        // AgentHost also subscribes to ProjectSelected to drive
        // the context-budget recompute + status message. The host
        // keeps the conversation list refresh here because the
        // sidebar / conversation VMs are its concern. The two
        // handlers are independent — both fire on the same event.
        _conversationList.Refresh(_sidebar.CurrentProject);
    }

    private void OnSidebarProjectAdded(object? sender, ProjectAddedEventArgs args)
    {
        _conversationList.Refresh(_sidebar.CurrentProject);
    }

    private void OnConversationSelected(object? sender, ConversationSelectedEventArgs args)
    {
        ActivityFeed.LoadConversation(args.Conversation);
        // Persist the selection so the next launch can restore the
        // same conversation. AppSettings.LastActiveConversationId
        // has been a real schema field since the AppSettings file
        // landed but no code read it — Refresh() in
        // ConversationListViewModel already accepts a
        // preferredConversationId, and AgentRunnerViewModel uses it
        // to highlight a freshly-created conversation; restore-from-
        // settings is the last consumer to wire up. null args
        // (= 'new' or unknown id) clears the pointer so we don't
        // land on a stale id after the user explicitly starts fresh.
        _settings.LastActiveConversationId = args.Conversation?.Id ?? "";
        // Fire-and-forget the save. Best-effort — a failed write
        // means we lose the restore pointer for one session but
        // nothing else breaks. async void is unsafe in event
        // handlers, hence the explicit ContinueWith to swallow any
        // exception the await might surface.
        _ = _repository.SaveSettingsAsync(_settings)
            .ContinueWith(task =>
            {
                _ = task.Exception; // observe + discard
            }, TaskScheduler.Default);
        StatusMessage = args.StatusMessage;
    }

    // 1.0 Beta: command palette surface. Each CommandItem carries a
    // lucide-style glyph, a one-line description, the keyboard shortcut
    // hint, and an async action that returns true if the palette should
    // close after running.
    private void RegisterCommandPaletteCommands()
    {
        CommandPalette.RegisterCommands(
        [
            new CommandItem(
                "打开设置",
                "配置模型提供方、API Key、Base URL",
                "⌘ ,",
                "M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z",
                () => { IsSettingsOpen = true; return Task.FromResult(true); }),
            new CommandItem(
                "切换主题",
                "在浅色 / 深色 / 跟随系统之间循环",
                "⌘ ⇧ T",
                "M12 4 V2 M12 22 V20 M4 12 H2 M22 12 H20 M5.5 5.5 L4.1 4.1 M19.9 19.9 L18.5 18.5 M5.5 18.5 L4.1 19.9 M19.9 4.1 L18.5 5.5 M12 8 a4 4 0 1 0 0 8 a4 4 0 1 0 0 -8",
                () => { _theme.CycleToNext(); return Task.FromResult(true); }),
            new CommandItem(
                "刷新状态",
                "从本地仓库重新读取项目和会话",
                "F5",
                "M3 12 a9 9 0 1 0 9 -9 a9.75 9.75 0 0 0 -6.74 2.74 L3 8 M3 3 V8 H8 M12 7 V12 L16 14",
                async () => { await RefreshAsync(); return true; }),
            new CommandItem(
                "新建对话",
                "清空当前活动，开始一个全新会话",
                "⌘ N",
                "M12 5 V19 M5 12 H19",
                () => { NewConversation(); return Task.FromResult(true); }),
            new CommandItem(
                "添加项目",
                "从本地选择一个新的代码仓库",
                "⌘ O",
                "M4 4 H20 V20 H4 Z M4 9 H20",
                async () =>
                {
                    var path = await _projectPicker.PickProjectFolderAsync();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        await AddProjectFromUiAsync(path);
                    }
                    return true;
                }),
            new CommandItem(
                "切换只读模式",
                "禁止 AIChat 修改项目中的任何文件",
                "⌘ ⇧ R",
                "M5 12 a7 7 0 1 1 14 0 a7 7 0 1 1 -14 0 M3 3 L21 21",
                () => { NoWriteMode = !NoWriteMode; return Task.FromResult(true); }),
            new CommandItem(
                "切换自动验证",
                "修改完成后自动运行检查命令",
                "⌘ ⇧ V",
                "M5 12 l4 4 L19 6",
                () => { _settingsViewModel.AutoVerify = !_settingsViewModel.AutoVerify; return Task.FromResult(true); }),
            new CommandItem(
                "测试当前模型",
                "发起一次连接性测试，确认 API Key 有效",
                "⌘ T",
                "M3 12 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 M12 7 V12 L16 14",
                async () =>
                {
                    await _provider.TestProviderCommand.ExecuteAsync(null);
                    return true;
                }),
            new CommandItem(
                "打开 Memory 编辑器",
                "查看、添加、删除当前项目的 memory 记录",
                "⌘ ⇧ M",
                "M4 4 H20 V20 H4 Z M4 9 H20 M9 9 V20",
                () => { OpenMemoryEditor(); return Task.FromResult(true); }),
            new CommandItem(
                "打开 Git 状态",
                "查看当前项目的修改文件与 diff",
                "⌘ ⇧ G",
                "M3 12 a9 9 0 1 0 3 -6.7 M3 4 v5 h5",
                async () =>
                {
                    await OpenGitStatusAsync();
                    return true;
                }),
            new CommandItem(
                "复制最后一条 AI 回复",
                "把最近一条 assistant 消息放到剪贴板",
                "⌘ ⇧ C",
                "M9 5 H7 a2 2 0 0 0 -2 2 v12 a2 2 0 0 0 2 2 h10 a2 2 0 0 0 2 -2 V7 a2 2 0 0 0 -2 -2 h-2 M9 5 a2 2 0 0 1 2 -2 h2 a2 2 0 0 1 2 2 v0 a2 2 0 0 1 -2 2 h-2 a2 2 0 0 1 -2 -2 z",
                async () =>
                {
                    // Same shape as the /copy slash command so the
                    // palette and the prompt input give identical
                    // feedback (system bubble confirms + char count).
                    var prompt = "/copy";
                    var (handled, result) = await AIChat.App.Avalonia.ViewModels.SlashCommandHandler.TryExecuteAsync(prompt, this);
                    if (handled && result is not null)
                    {
                        ActivityFeed.Add(new ActivityItemViewModel(result.Title, result.Body, "命令"));
                        StatusMessage = result.Title + "。";
                    }
                    return true;
                }),
            new CommandItem(
                "显示命令面板",
                "搜索命令、动作、设置",
                "⌘ K",
                "M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z",
                () => { IsCommandPaletteOpen = true; return Task.FromResult(false); }),
        ]);
    }

    private void OnApprovalPresented(object? sender, ToolApprovalPresentedEventArgs args)
    {
        _activeApprovalBubble = new ActivityItemViewModel(
            "需要确认",
            args.Request.Preview.Summary,
            "等待");
        ActivityFeed.Add(_activeApprovalBubble);
        StatusMessage = args.StatusMessage;
    }

    private void OnApprovalResolved(object? sender, ToolApprovalResolvedEventArgs args)
    {
        var title = args.Decision.IsApproved ? "已允许操作" : "已拒绝操作";
        var detail = args.Decision.IsApproved ? "AIChat 可以继续。" : args.Decision.Reason;
        var status = args.Decision.IsApproved ? "已允许" : "已拒绝";

        // Update the bubble the presented handler dropped, if it's
        // still in the feed. If the feed was cleared between
        // presented and resolved, fall through to a fresh row.
        if (_activeApprovalBubble is { } bubble &&
            ActivityFeed.Activity.Contains(bubble))
        {
            bubble.Title = title;
            bubble.Detail = detail;
            bubble.Status = status;
        }
        else
        {
            ActivityFeed.Add(new ActivityItemViewModel(title, detail, status));
        }
        _activeApprovalBubble = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Same RelayCommand-exception-escape risk as SendTaskAsync
        // (d7b179c): F5 (KeyBinding) and the palette both invoke
        // RefreshCommand directly with no SafeRun wrapper. The body
        // touches settings + projects + JSON normalization; any of
        // them can throw (corrupt file, permission denied, removed
        // drive). Catch and surface to the status bar so the user
        // sees what happened instead of the app silently dying.
        StatusMessage = "正在读取本地状态...";
        try
        {
            _settings = await _repository.LoadSettingsAsync();
            _settingsHolder.Replace(_settings);
            // Apply the persisted theme now that we have the loaded settings.
            _theme.Apply(_settings.ThemePreference);
            ProviderSettingsService.Normalize(_settings, _settings.Temperature);
            // Clamp the persisted knobs to their valid ranges. Inlined
            // here (vs. a separate settings service) so the only
            // caller — the constructor — and the rules live in the
            // same place. ToolSettingsService still owns tool-list
            // normalization because that one varies per registered
            // tool catalog.
            _settings.AgentMaxToolRounds = Math.Clamp(_settings.AgentMaxToolRounds, 1, 100);
            _settings.MaxAutoFixRounds = Math.Clamp(_settings.MaxAutoFixRounds, 0, 10);
            _settings.RetryMaxAttempts = Math.Clamp(_settings.RetryMaxAttempts, 0, 10);
            _settings.MaxOutputTokens = Math.Clamp(_settings.MaxOutputTokens, 256, 32768);
            _settings.ConversationContextRatio = Math.Clamp(_settings.ConversationContextRatio, 0.3, 1.0);
            ToolSettingsService.Normalize(_settings, _toolRegistry);

            var projects = (await _repository.LoadProjectsAsync()).ToList();
            var active = ProviderSettingsService.GetSelectedProvider(_settings);

            ActiveProvider = active is null ? "未配置模型" : active.Name;
            ActiveModel = active is null ? "配置模型后即可运行任务" : active.SelectedModelId;
            Readiness = active is not null && !string.IsNullOrWhiteSpace(active.ApiKey) ? "可运行" : "需要密钥";

            // The settings-modal mirror (Temperature / MaxOutputTokens /
            // AgentExecutionMode / AutoVerify / Tools permission matrix)
            // is owned by SettingsViewModel. Its Refresh() seeds the
            // mirrors from _settingsHolder.Current; the per-field
            // skip-if-same-value guards on the OnXxxChanged partials
            // keep the load-time assignment from firing a save. The
            // page-header pill and the settings modal both bind to
            // Settings.AutoVerify, so the host doesn't need a local
            // mirror anymore.
            _settingsViewModel.Refresh();

            _sidebar.Refresh(projects);
            // Restore the last-active conversation if its id still
            // matches a conversation on the current project.
            // ConversationListViewModel.Refresh already handles the
            // "preferred id not found" case by falling back to the most
            // recent conversation / "new" placeholder, so a stale id
            // from a deleted conversation degrades silently rather
            // than throwing.
            _conversationList.Refresh(_sidebar.CurrentProject, _settings.LastActiveConversationId);
            _provider.Refresh();
            _settingsViewModel.Refresh();
            // Recompute the context budget after the settings +
            // project load lands — AgentHost owns the recompute
            // and the meter, the host just kicks the initial
            // pass.
            _ = _agentHost.RecomputeContextInputTokensAsync(_agentHost.DraftPrompt);

            if (ActivityFeed.Activity.Count == 0)
            {
                ActivityFeed.Clear();
            }

            StatusMessage = "已加载。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"刷新失败：{ex.Message}";
        }
    }


    // PR-3: project list, selection, and add logic live in ProjectSidebarViewModel.
    // These two passthroughs keep the XAML code-behind talking to a single
    // view-model.
    public Task SelectProjectFromUiAsync(string projectId)
        => _sidebar.SelectProjectAsync(projectId);

    public Task AddProjectFromUiAsync(string projectPath)
        => _sidebar.AddProjectAsync(projectPath);

    public Task SelectConversationFromUiAsync(string conversationId)
    {
        _conversationList.SelectConversation(conversationId);
        return Task.CompletedTask;
    }

    // PR-2: handlers for events raised by ProviderConfigViewModel. They keep
    // the parent VM's display state in sync without re-architecting the
    // cross-VM contract.
    private void OnProviderSaved(object? sender, ProviderSavedEventArgs args)
    {
        if (args.ErrorMessage is not null)
        {
            StatusMessage = args.ErrorMessage;
            return;
        }

        ActiveProvider = args.ProviderName;
        ActiveModel = args.ModelId;
        Readiness = "可运行";
        StatusMessage = args.AlreadyExisted ? "已更新模型配置。" : "已保存模型配置。";
    }

    // Tracks the "正在连接 X" bubble dropped by OnProviderTestStarted
    // so the completion handler can update it in place (Detail + Status)
    // instead of appending a second bubble. The earlier shape was
    // 'add a '正在连接/运行中' bubble on start, add a second
    // '测试通过/失败' bubble on completion' — which left the first
    // bubble stuck at '运行中' forever, so the user saw two
    // model-test rows for every test: a stale in-flight one and
    // the real outcome. Same pattern AgentRunnerViewModel uses for
    // the assistant bubble (HasReceivedFirstContent + Detail +=
    // ContentDelta).
    private ActivityItemViewModel? _activeTestBubble;

    // Same in-place update pattern for tool-approval bubbles:
    // OnApprovalPresented drops '需要确认 / 等待', OnApprovalResolved
    // would otherwise drop a second '已允许操作 / 已允许' or
    // '已拒绝操作 / 已拒绝' row. The first row's status stayed
    // '等待' forever after the user decided — the approval modal
    // is the primary surface, so the stale bubble is more noise
    // than information. Track the row, mutate it on resolve.
    private ActivityItemViewModel? _activeApprovalBubble;

    private void OnProviderTestStarted(object? sender, ProviderTestStartedEventArgs args)
    {
        // Don't touch IsRunning here — that's the agent-run
        // indicator driving send/stop button visibility, the
        // status-bar context meter, and CanRetry / CanStopTask.
        // A connection test is a one-shot background probe; it
        // shares none of those surfaces. The earlier code set
        // IsRunning = true here (and back to false in
        // OnProviderTestCompleted), which made the send / stop
        // button pair flip-flop while a test was in flight —
        // confusing at best, and dangerous when the user
        // happened to be running an agent at the same time:
        // the test completion would clobber IsRunning back to
        // false, the send button would re-enable, and the user
        // could kick off a second agent run against a
        // still-in-flight first one.
        //
        // The send button does still need to disable during a
        // test (otherwise the user can race a fresh agent run
        // against the in-flight probe). The new IsProviderTesting
        // flag is the dedicated gate for that; CanSendTask on
        // AgentHost now checks both !IsRunning AND
        // !IsProviderTesting.
        IsProviderTesting = true;
        StatusMessage = $"正在测试 {args.ProviderName}...";
        _activeTestBubble = new ActivityItemViewModel(
            "模型测试",
            $"正在连接 {args.ProviderName} ({args.ModelId})",
            "运行中");
        ActivityFeed.Add(_activeTestBubble);
    }

    private void OnProviderTestCompleted(object? sender, ProviderTestCompletedEventArgs args)
    {
        // Don't touch IsRunning here either — see the long comment
        // on OnProviderTestStarted. The pair of IsRunning flips
        // around the test is the surface bug: a connection test
        // is not an agent run and must not touch the agent
        // surface state.
        //
        // Drop the IsProviderTesting gate here, paired with the
        // set in OnProviderTestStarted. NotifyCanExecuteChangedFor
        // on the field re-evaluates CanSendTask so the send
        // button re-enables immediately.
        IsProviderTesting = false;
        var status = args.Exception is not null
            ? "失败"
            : args.IsSuccess ? "通过" : "失败";
        var detail = args.Exception is not null
            ? $"测试失败：{args.Message}"
            : args.Message;

        // Update the bubble the started handler dropped, if it's
        // still in the feed. The feed could have been cleared
        // (/clear, /new, "新对话" button) between started and
        // completed — in which case the field is stale, fall
        // through to adding a fresh row.
        if (_activeTestBubble is { } bubble &&
            ActivityFeed.Activity.Contains(bubble))
        {
            bubble.Detail = detail;
            bubble.Status = status;
        }
        else
        {
            ActivityFeed.Add("模型测试", detail, status);
        }
        _activeTestBubble = null;

        Readiness = args.IsSuccess ? "可运行" : "需检查";
        StatusMessage = args.IsSuccess ? "模型连接正常。" : "模型连接失败。";
    }

    // PR-12: ShowNewConversation and ApplyConversationToActivity moved to
    // ActivityFeedViewModel. The parent VM only orchestrates
    // ActivityFeed.LoadConversation via the OnConversationSelected handler.


    // PR-6: Approve / Reject commands live on ToolApprovalViewModel.

    // PR-13: RunAgentTaskAsync, ApplyAgentEventAsync, FriendlyToolSummary,
    // SaveProjectsAsync, and BuildProjectSnapshot all moved to
    // AgentRunnerViewModel. The host VM only validates input and calls
    // _agentRunner.RunAsync(prompt, effectiveSettings).
}
