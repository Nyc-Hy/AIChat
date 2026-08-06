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
using AIChat.Application.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the sidebar "最近" section. Just a title — clicking
// routes to the conversation / search the user was last on.
// Wave 6 replaces this with a real conversation id / search query
// 1.0.1: per-row "最近" sidebar item. The
// previous shape had only a Title and the
// click was a placeholder toast — a daily-
// driver user clicking a "最近" entry
// expected to jump to that conversation, not
// see a "Wave 6 接入" stub. ConversationId
// is the ConversationCardViewModel.Id the
// click handler routes through
// ConversationList.SetSelectedConversation
// to drive the existing
// ConversationSelected event (which the
// activity feed + status message already
// hook into). Title stays as the display
// label so the XAML doesn't need a
// ConversationCard-shaped template.
public sealed class RecentItemViewModel(string title, string conversationId, string updatedAtDisplay)
{
    public string Title { get; } = title;
    public string ConversationId { get; } = conversationId;
    // 1.0.1: the "最近" sidebar section's
    // XAML now binds a muted sub-text to
    // this so the user can tell two
    // conversations with the same title
    // apart, and so a stale "8月1日"
    // entry doesn't masquerade as
    // fresh alongside a "今天" entry.
    // Format matches the conversation
    // card row in the "对话" section
    // (M月d日 HH:mm, toLocalTime) so
    // the two surfaces read the same.
    public string UpdatedAtDisplay { get; } = updatedAtDisplay;
}

public sealed partial class MainWindowViewModel : ViewModelBase, ISlashCommandHost
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
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
    private readonly PluginsViewModel _pluginsViewModel;
    private readonly ScheduledViewModel _scheduledViewModel;
    private readonly SitesViewModel _sitesViewModel;
    private readonly AIChat.Application.Workspace.IWorkspaceChangeService _workspace;
    private readonly AgentHostViewModel _agentHost;

    // lastAssistantStatus + CanRetry + _lastUserPrompt all moved
    // to AgentHostViewModel — see the AgentHost property. The host
    // doesn't need a local mirror; XAML binds to
    // AgentHost.CanRetry / AgentHost.LastAssistantStatus.

    private AppSettings _settings = new();

    // App-wide status / readiness surface (provider, model,
    // readiness pill, in-flight test flag, derived greeting /
    // has-project / status-bar copy). Split out so the host
    // doesn't carry the 4 backing fields + 6 computed
    // properties + Sidebar subscription plumbing. See
    // AppStatusViewModel for the full surface and the
    // PropertyChanged forwarding for IsProviderTesting that
    // re-evaluates AgentHost's send / stop CanExecute.
    private readonly AppStatusViewModel _appStatus;
    public AppStatusViewModel AppStatus => _appStatus;

    // ISlashCommandHost forwarders — the slash handler reads
    // ActiveProvider / ActiveModel off the host interface, and
    // the live values now live on AppStatusViewModel. Keep the
    // ISlashCommandHost surface unchanged so the handler's
    // contract doesn't need to know about the extraction.
    public string ActiveProvider => _appStatus.ActiveProvider;
    public string ActiveModel => _appStatus.ActiveModel;

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
        // Sprint 0.5: mirror NoWriteMode onto DefaultAccess so the
        // existing ⌘⇧R shortcut still controls the Codex-aligned
        // 2-toggle model. ⌘⇧R becomes "toggle DefaultAccess".
        if (DefaultAccess == value)
        {
            DefaultAccess = !value;
        }
    }

    partial void OnDefaultAccessChanged(bool value)
    {
        if (NoWriteMode == value)
        {
            NoWriteMode = !value;
        }
        OnPropertyChanged(nameof(PermissionBadgeText));
        OnPropertyChanged(nameof(PermissionBadgeTooltip));
        PersistPermissionSettings();
    }

    partial void OnFullAccessEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(PermissionBadgeText));
        OnPropertyChanged(nameof(PermissionBadgeTooltip));
        PersistPermissionSettings();
    }

    partial void OnEnvironmentPanelOpenChanged(bool value)
    {
        _settings.EnvironmentPanelOpen = value;
        _ = PersistSettingsFireAndForget();
    }

    // Clipboard helpers used by the /copy slash command. HasClipboardService
    // lets the slash handler fail gracefully when the platform clipboard
    // isn't wired (e.g. during tests where no TopLevel has been set).
    public bool HasClipboardService => _clipboard.IsAvailable;

    public Task CopyToClipboardAsync(string text) => _clipboard.SetTextAsync(text);

    // 1.0.1: copy the full content of an AI
    // bubble to the clipboard. Routed through
    // the host (not ActivityItemViewModel
    // directly) so the per-bubble VM doesn't
    // need a clipboard service reference —
    // matches the same pattern /copy and
    // ExportConversationMenuItem use. Returns
    // the number of characters actually copied
    // (0 if the input was empty / clipboard
    // unavailable) so the XAML click handler
    // can decide what toast to show without
    // re-reading state.
    public async Task<int> CopyAssistantBubbleAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        if (!_clipboard.IsAvailable)
        {
            return 0;
        }
        await _clipboard.SetTextAsync(text);
        return text.Length;
    }

    // Git status helper used by the /git-status slash command. Renders
    // the current project's branch + a compact change list as a single
    // string the host can drop into the activity feed. The full
    // WorkspaceChangeService handles the underlying git call; this
    // method is the presentation layer.
    public async Task<string> GetGitStatusSummaryAsync()
    {
        var project = _sidebar.CurrentProject;
        if (project is null || string.IsNullOrWhiteSpace(project.TryGetPrimaryPath()))
        {
            return "(请先选择项目)";
        }

        AIChat.Application.Workspace.WorkspaceChangeSet changeSet;
        try
        {
            changeSet = await _workspace.GetChangesAsync(project.TryGetPrimaryPath());
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

    // Wave 8 (parity plan §7 Wave 8): Plugins modal. Same
    // pattern as Settings / MemoryEditor / GitStatus / RunHistory
    // — a single bool the XAML binds to. Opens via
    // OpenPluginsCommand (the sidebar's 5th nav item).
    [ObservableProperty]
    private bool isPluginsOpen;

    // Wave 9 (parity plan §7 Wave 9): Scheduled + Sites modals.
    // Same pattern as Plugins. OpenScheduledCommand /
    // OpenSitesCommand are wired to the 3rd / 4th sidebar nav
    // items; the underlying VMs are constructed eagerly in DI
    // so the registry's first ReloadAsync fires before the
    // user opens the modal.
    [ObservableProperty]
    private bool isScheduledOpen;

    [ObservableProperty]
    private bool isSitesOpen;

    // Memory editor modal: full add / delete UI for the current
    // project's memory. ⌘⇧M opens it. /memory (slash) stays as a
    // quick read-only summary in the activity feed — this is the
    // edit surface.
    [ObservableProperty]
    private bool isMemoryEditorOpen;

    public MemoryEditorViewModel MemoryEditor => _memoryEditor;

    public PluginsViewModel Plugins => _pluginsViewModel;
    public ScheduledViewModel Scheduled => _scheduledViewModel;
    public SitesViewModel Sites => _sitesViewModel;

    // Git status / diff viewer modal. ⌘⇧G opens it; ⌘G stays as the
    // quick /git bubble for the lightweight "what just changed"
    // glance.
    [ObservableProperty]
    private bool isGitStatusOpen;

    public GitStatusViewModel GitStatus => _gitStatus;

    [ObservableProperty]
    private bool isRunHistoryOpen;

    public RunHistoryViewModel RunHistory { get; }

    // ---- Sprint 0.5: right-side Environment panel + 2-toggle permissions ----
    // The Environment panel hosts the live git / sub-agent / source sections
    // that used to live in the GitStatusView modal + the plan panel. The
    // 2-toggle permissions replace the single NoWriteMode bool with the
    // Codex Desktop shape: `DefaultAccess` (workspace writes + ask for
    // network) and `FullAccessEnabled` (no approvals). Both off = read-only.
    // See docs/CODEX_DESKTOP_PARITY_PLAN.md §13.5 deviation #1.
    public EnvironmentPanelViewModel EnvironmentPanel => _environmentPanel;

    // 1.0.1: "最近" section in the sidebar.
    // Previously a hard-coded 9-item demo list
    // with a placeholder toast on click
    // (\"Wave 6 接入\"). Now mirrors
    // ConversationList.Conversations (which
    // itself is sorted UpdatedAt desc by
    // ConversationListViewModel.Refresh) —
    // the same 8-row projection the \"对话\"
    // section shows, just rendered without
    // the inline-rename + selected-style
    // surface so the two sections read as
    // distinct lists in the sidebar. The
    // CollectionChanged subscription keeps
    // the projection in lock-step when the
    // user adds / renames / deletes a
    // conversation.
    public ObservableCollection<RecentItemViewModel> RecentItems { get; } = new();

    // 1.0.1: rebuild the \"最近\" projection from
    // ConversationList.Conversations. Called
    // from the CollectionChanged subscription
    // in the ctor; also callable from a test
    // that adds / removes conversations
    // directly. The Clear + re-add pattern
    // matches what the EnvironmentPanel does
    // for its Sources list — the count is
    // small (max 8) so the cost is
    // negligible and the alternative
    // (incremental diff) is more code than
    // the win is worth.
    private void RecomputeRecentItems()
    {
        RecentItems.Clear();
        // The "最近" list is rendered as a
        // simple row (no inline rename, no
        // selected style, no context
        // flyout) so we don't need a full
        // ConversationCardViewModel for
        // each entry — just Title + Id +
        // UpdatedAtDisplay. The XAML shows
        // UpdatedAtDisplay as the muted
        // sub-text so the user can tell
        // two same-titled conversations
        // apart, and so a stale "8月1日"
        // entry doesn't masquerade as
        // fresh alongside a "今天" entry.
        foreach (var card in _conversationList.Conversations.Take(8))
        {
            // The card's Detail field is the
            // already-formatted UpdatedAt
            // string (the conversation list
            // section renders it as a muted
            // sub-text on the same row) —
            // re-use it here so the two
            // surfaces read the same.
            RecentItems.Add(new RecentItemViewModel(
                title: card.Title,
                conversationId: card.Id,
                updatedAtDisplay: card.Detail));
        }
    }

    private EnvironmentPanelViewModel _environmentPanel = null!;

    [ObservableProperty]
    private bool environmentPanelOpen = true;

    [ObservableProperty]
    private bool defaultAccess = true;

    [ObservableProperty]
    private bool fullAccessEnabled;

    // Computed display state for the composer permission badge. 3 states,
    // mapped to 3 display strings the composer XAML can show verbatim.
    // Order matters: FullAccess wins over DefaultAccess because it's the
    // strictly broader grant.
    public string PermissionBadgeText =>
        FullAccessEnabled ? "完全访问"
        : DefaultAccess ? "默认访问"
        : "只读";

    public string PermissionBadgeTooltip =>
        FullAccessEnabled ? "完全访问 — 无需批准即可写入和执行网络命令 (点击切换)"
        : DefaultAccess ? "默认访问 — 工作区写入需要批准 (点击切换)"
        : "只读 — 不修改项目，不执行网络命令 (点击切换)";

    // First-level sidebar nav: 5 entries from plan §4. Only "新对话" is
    // wired in Sprint 0.5; the other 4 are no-op stubs with a "Wave X"
    // tag rendered in the XAML so the user sees them as "coming soon"
    // rather than placeholders-without-backing (plan §5.4).
    [RelayCommand]
    private void NewChat() => NewConversation();

    [RelayCommand]
    private void OpenPullRequests()
        // 1.0.1: previous shape was
        // "拉取请求 — Wave 6 暂未开放"
        // — Wave 6 shipped Git
        // status / commit / restore,
        // not GitHub OAuth. The
        // actual blocker is the
        // GitHub OAuth flow (P1
        // deferred per
        // SHIP_REPORT §4). Toast
        // message swapped to the
        // honest "needs GitHub
        // OAuth" wording so a
        // daily-driver user
        // doesn't re-wonder why
        // the button is disabled
        // every time they mouse
        // over the sidebar.
        => _toast.Show("拉取请求 — 需要 GitHub OAuth (P1 deferred)", ToastLevel.Info);

    [RelayCommand]
    private void OpenSites()
    {
        if (!CanOpenModal())
        {
            return;
        }
        IsSitesOpen = true;
    }

    [RelayCommand]
    private void CloseSites() => IsSitesOpen = false;

    [RelayCommand]
    private void OpenScheduled()
    {
        if (!CanOpenModal())
        {
            return;
        }
        IsScheduledOpen = true;
    }

    [RelayCommand]
    private void CloseScheduled() => IsScheduledOpen = false;

    [RelayCommand]
    private void OpenPlugins()
    {
        // Wave 8 (parity plan §7 Wave 8): open the Plugins modal.
        // The first slice is read-only (list installed + reload),
        // so we just flip the IsPluginsOpen flag the same way
        // Settings / MemoryEditor / GitStatus do. The modal's
        // content (PluginsView) binds to PluginsViewModel which
        // is constructed eagerly from the registered
        // IPluginRegistry — see AppHost / ServiceRegistration.
        if (!CanOpenModal())
        {
            return;
        }
        IsPluginsOpen = true;
    }

    [RelayCommand]
    private void ClosePlugins() => IsPluginsOpen = false;

    [RelayCommand]
    private void ToggleEnvironmentPanel()
    {
        EnvironmentPanelOpen = !EnvironmentPanelOpen;
        _settings.EnvironmentPanelOpen = EnvironmentPanelOpen;
        _ = PersistSettingsFireAndForget();
    }

    // The badge is a clickable chip. Cycle through the 3 permission
    // states on each click:
    //   default-access → full-access → read-only → default-access
    // This matches the Codex convention of clicking the badge to open
    // a quick toggle. The 2-toggle settings page (Wave 4 / 10) will be
    // the more discoverable surface; this is the keyboard-fast path.
    [RelayCommand]
    private void CyclePermissionState()
    {
        if (DefaultAccess && !FullAccessEnabled)
        {
            FullAccessEnabled = true;
        }
        else if (FullAccessEnabled)
        {
            DefaultAccess = false;
            FullAccessEnabled = false;
        }
        else
        {
            DefaultAccess = true;
            FullAccessEnabled = false;
        }
        PersistPermissionSettings();
        _ = _agentHost.RecomputeContextInputTokensAsync(_agentHost.DraftPrompt);
    }

    private void PersistPermissionSettings()
    {
        _settings.DefaultAccess = DefaultAccess;
        _settings.FullAccessEnabled = FullAccessEnabled;
        _ = PersistSettingsFireAndForget();
    }

    private async Task PersistSettingsFireAndForget()
    {
        try
        {
            await _repository.SaveSettingsAsync(_settings);
        }
        catch
        {
            // Best-effort. The user can recover next launch by re-toggling.
        }
    }

    // Keyboard-shortcuts cheat sheet modal. The "?" titlebar button
    // and the ⌘/ global shortcut both surface this — both paths
    // route through OpenShortcutsCommand / CloseShortcutsCommand
    // so the modals always open to a fresh state.
    [ObservableProperty]
    private bool isShortcutsOpen;

    // Approximate context window + estimated input tokens + the
    // status-bar context meter all moved to AgentHostViewModel
    // (it's the run state). The XAML binds through AgentHost.
    //
    // Status-bar text + Greeting/SubGreeting/HasProject/IsReady/
    // NeedsConfiguration all moved to AppStatusViewModel
    // (it's the "app is in what state" surface). The XAML binds
    // through AppStatus.

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
        if (!CanOpenModal())
        {
            return;
        }

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
    private void OpenSettings()
    {
        if (CanOpenModal())
        {
            IsSettingsOpen = true;
        }
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void OpenMemoryEditor()
    {
        if (!CanOpenModal())
        {
            return;
        }

        if (!HasSelectedProject())
        {
            StatusMessage = "请先选择一个项目，再编辑项目记忆。";
            _toast.Show("请先选择一个项目。", ToastLevel.Info);
            return;
        }

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
        if (!CanOpenModal())
        {
            return;
        }

        if (!HasSelectedProject())
        {
            StatusMessage = "请先选择一个项目，再查看 Git 状态。";
            _toast.Show("请先选择一个项目。", ToastLevel.Info);
            return;
        }

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
    private void OpenRunHistory()
    {
        if (!CanOpenModal())
        {
            return;
        }

        if (!HasSelectedProject())
        {
            StatusMessage = "请先选择一个项目，再查看运行记录。";
            _toast.Show("请先选择一个项目。", ToastLevel.Info);
            return;
        }

        RunHistory.Refresh();
        IsRunHistoryOpen = true;
    }

    [RelayCommand]
    private void CloseRunHistory() => IsRunHistoryOpen = false;

    // Keyboard-shortcuts cheat sheet modal. The "?" titlebar button
    // and the ⌘/ global shortcut both go through OpenShortcuts so
    // the user can always reach the list (the global shortcut is
    // the user-discoverable path; the titlebar button is the
    // always-visible entry point for first-time users).
    [RelayCommand]
    private void OpenShortcuts()
    {
        if (CanOpenModal())
        {
            IsShortcutsOpen = true;
        }
    }

    private bool CanOpenModal() => !_approvalViewModel.HasPendingApproval;

    private bool HasSelectedProject() =>
        _sidebar.CurrentProject is { } p && !string.IsNullOrEmpty(p.TryGetPrimaryPath());

    [RelayCommand]
    private void CloseShortcuts() => IsShortcutsOpen = false;

    // Standalone conversation list (Wave 3: plan §3.1 普通聊天)。
    // Standalone sessions 不绑 project — 跑不需要项目工具的功能(查资料、
    // 写脚本、问问题)。在 sidebar 独立 section 显示,跟 project
    // sessions 分开,UI 不会把 "新对话" 的草稿串到项目会话。
    public ObservableCollection<ConversationCardViewModel> StandaloneConversations { get; } = [];

    // Standalone 与 Project 分两条路径走:Project 走 ConversationListViewModel
    // (按 project filter),Standalone 走 MainWindowViewModel 自己(全 app
    // 共一份)。这个 list 给 sidebar "Standalone" section 直接绑。
    private IReadOnlyList<Standalone> _standaloneSessions = [];
    // 2026-08-03: cache the full cross-project session list for
    // /search. MainWindowViewModel owns the lifetime of the
    // cache (RefreshAsync refreshes it, RemoveConversationAsync
    // invalidates it) so the slash handler does not have to
    // hit disk on every search invocation. Empty list is
    // safe — the search command treats an empty cache as
    // 'no sessions yet', which is correct for a fresh install.
    private IReadOnlyList<ChatSession> _allSessions = [];
    public IReadOnlyList<ChatSession> AllSessions => _allSessions;

    [RelayCommand]
    private void NewConversation()
    {
        _agentHost.ClearPreparedRunLink();
        ActivityFeed.Clear();
        StatusMessage = "新对话。";
        // 1.0.1: the user's intent on ⌘N
        // or "新对话" is "I want to type
        // something now" — the
        // Click-after-Click-to-focus
        // dance adds friction. Raise
        // FocusComposerRequested so
        // MainWindow.xaml.cs can put
        // the caret in PromptInput. The
        // same hook fires on the
        // ConversationSelected event
        // path when the "new" placeholder
        // card is the selection (a user
        // who re-clicks the same "new"
        // card mid-session gets the
        // same focus affordance).
        FocusComposerRequested?.Invoke(this, EventArgs.Empty);
    }

    // 1.0.1: the host raises this when the
    // composer's input should be the
    // user's next focus. MainWindow.xaml.cs
    // subscribes in its constructor and
    // calls FocusPromptInput (which also
    // SelectAll so the user can start
    // typing immediately, the same
    // browser-address-bar convention ⌘L
    // already uses). Kept as an event
    // rather than a method on the VM so
    // the VM doesn't take a direct
    // dependency on the Avalonia
    // visual tree (FocusPromptInput
    // needs PromptInput, which is a
    // XAML element only the code-behind
    // can reach).
    public event EventHandler? FocusComposerRequested;

    [RelayCommand]
    private async Task NewStandaloneConversationAsync()
    {
        // Wave 3: ⌘N 真正创建并持久化一个 Standalone ChatSession,
        // 让"新对话"不是一个 ephemeral placeholder。
        // 1. 创建 domain 对象
        var session = new Standalone
        {
            Title = "新任务",
            UpdatedAt = DateTimeOffset.Now,
        };
        // 2. 写盘(append 进 sessions.json 的 Standalone 列表)
        var all = (await _repository.LoadSessionsAsync()).ToList();
        all.Add(session);
        await _repository.SaveSessionsAsync(all);
        _standaloneSessions = all.OfType<Standalone>().ToList();
        // 3. 推到 sidebar
        StandaloneConversations.Insert(0, MakeStandaloneCard(session));
        // 4. 清 agent 草稿 + activity feed,等用户开始打字
        _agentHost.ClearPreparedRunLink();
        ActivityFeed.Clear();
        StatusMessage = "新对话。";
        _toast.Show("新对话已创建 — 不绑定项目,直接开始。", ToastLevel.Info);
        // 5. highlight the new card so the
        // user immediately sees which row
        // was created. The project-scoped
        // conversation list does this
        // automatically (SetSelectedConversation
        // walks the collection); the
        // Standalone list was wired with a
        // .selected class binding but
        // never had any code that
        // toggled IsSelected, so the
        // user had no visual confirmation
        // the click had registered.
        SetSelectedStandaloneCard(session.Id);
    }

    private ConversationCardViewModel MakeStandaloneCard(Standalone session)
    {
        return new ConversationCardViewModel(
            session.Id,
            string.IsNullOrWhiteSpace(session.Title) ? "新任务" : session.Title,
            session.UpdatedAt.ToLocalTime().ToString("M月d日 HH:mm"),
            // Standalone 标题改完直接写盘(跟 project 一样);onTitleChange
            // 接 MainWindowViewModel.PersistStandaloneTitleAsync
            PersistStandaloneTitleAsync);
    }

    // Public: 给 ConversationCardViewModel 的重命名回调。
    public async Task PersistStandaloneTitleAsync(string sessionId, string newTitle)
    {
        var trimmed = newTitle?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return;
        var target = _standaloneSessions.FirstOrDefault(s =>
            string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.Title == trimmed) return;
        target.Title = trimmed;
        target.UpdatedAt = DateTimeOffset.Now;
        var all = (await _repository.LoadSessionsAsync()).ToList();
        // 同步更新 in-memory list + 写盘
        var saved = all.OfType<Standalone>().FirstOrDefault(s => s.Id == sessionId);
        if (saved is not null) saved.Title = trimmed;
        await _repository.SaveSessionsAsync(all);
        // sidebar card 同步
        var card = StandaloneConversations.FirstOrDefault(c => c.Id == sessionId);
        if (card is not null) card.Title = trimmed;
    }

    // 启动 + 切 sidebar 时刷新 standalone 列表
    public async Task RefreshStandaloneConversationsAsync()
    {
        var all = await _repository.LoadSessionsAsync();
        _standaloneSessions = all.OfType<Standalone>()
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
        // /search cache: refresh the full session list here so
        // the search slash command has a current view of every
        // project. Standalone-only today; a follow-up slice
        // adds Project sessions (the workspace change service
        // owns their storage path).
        _allSessions = all.OrderByDescending(s => s.UpdatedAt).ToList();
        StandaloneConversations.Clear();
        foreach (var session in _standaloneSessions)
        {
            StandaloneConversations.Add(MakeStandaloneCard(session));
        }
    }

    // Wave 3: load a Standalone session from disk + push to the
    // activity feed. Mirror of how OnConversationSelected (project
    // side) routes a project session to ActivityFeed.LoadConversation.
    public async Task OpenStandaloneConversationAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var all = await _repository.LoadSessionsAsync();
        var session = all.OfType<Standalone>().FirstOrDefault(s =>
            string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            StatusMessage = "找不到对应的 Standalone 对话。";
            return;
        }
        _agentHost.ClearPreparedRunLink();
        ActivityFeed.LoadConversation(session);
        StatusMessage = $"已打开 Standalone 对话：{session.Title}";
        // 1.0.1: mirror the
        // project-scoped list's
        // SetSelectedConversation —
        // the .selected class on
        // the Standalone row was
        // bound but never had a
        // code path that flipped
        // IsSelected, so the user
        // clicked a Standalone
        // card, saw the activity
        // feed load, but the row
        // stayed unhighlighted.
        // Same pattern the
        // 8749763 / 0ff4ed3 ships
        // normalised across the
        // two card surfaces.
        SetSelectedStandaloneCard(sessionId);
    }

    // 1.0.1: shared helper for
    // flipping IsSelected across
    // the Standalone list. Mirrors
    // ConversationListViewModel.
    // SetSelectedConversation so a
    // user clicking a Standalone
    // card (or ⌘N creating a new
    // one) gets the same
    // accent-tinted row the
    // project-scoped list gives
    // them. The toggle is
    // idempotent — selecting the
    // already-selected card is a
    // no-op, so callers don't
    // need a "did anything
    // change" guard.
    private void SetSelectedStandaloneCard(string? sessionId)
    {
        foreach (var card in StandaloneConversations)
        {
            card.IsSelected = card.Id == sessionId;
        }
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
        PluginsViewModel pluginsViewModel,
        ScheduledViewModel scheduledViewModel,
        SitesViewModel sitesViewModel,
        AIChat.Application.Workspace.IWorkspaceChangeService workspace,
        InputArtifactFileStore artifactFileStore,
        AIChat.Application.BackgroundProcesses.IBackgroundProcessSupervisor processSupervisor,
        ISourceRegistry sourceRegistry)
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
        _pluginsViewModel = pluginsViewModel;
        _scheduledViewModel = scheduledViewModel;
        _sitesViewModel = sitesViewModel;
        _workspace = workspace;

        // 1.0.1: keep the \"最近\" sidebar projection
        // in lock-step with ConversationList.Conversations.
        // ConversationList.Refresh() already sorts the
        // collection UpdatedAt desc, so a Clear + re-add
        // gives us the right order without a separate
        // sort. The handler runs synchronously (the
        // observable is mutated on the UI thread by
        // ConversationList, so no dispatcher hop is
        // needed); the projection itself is also small
        // (max 8 rows).
        _conversationList.Conversations.CollectionChanged += (_, _) => RecomputeRecentItems();
        RecomputeRecentItems();
        // App-status surface (active provider / model / readiness
        // pill / in-flight test flag / derived Greeting + HasProject
        // + StatusBarModel). Sidebar subscription lives inside
        // AppStatusViewModel; the only PropertyChanged we listen
        // for here is IsProviderTesting so we can re-evaluate the
        // agent host's send / stop CanExecute. (Can't use
        // [NotifyCanExecuteChangedFor] on AppStatus.IsProviderTesting
        // because the commands live on AgentHost, not on the
        // status VM.)
        _appStatus = new AppStatusViewModel(sidebar);

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
            sourceRegistry,
            setStatusMessage: value => StatusMessage = value,
            getSettings: () => _settings,
            getNoWriteMode: () => NoWriteMode,
            getIsProviderTesting: () => _appStatus.IsProviderTesting,
            artifactFileStore: artifactFileStore);
        _appStatus.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppStatusViewModel.IsProviderTesting))
            {
                _agentHost.SendTaskCommand.NotifyCanExecuteChanged();
                _agentHost.StopTaskCommand.NotifyCanExecuteChanged();
            }
        };

        // The slash-command handler is a small static helper that
        // currently expects the host VM (it reads /status fields off
        // it). Until the slash handler is also refactored to a
        // smaller surface, the host routes the call through a
        // single delegate. The reference lives on AgentHost so
        // SendTaskAsync's call site stays readable.
        _agentHost.RegisterSlashHandler(prompt =>
            SlashCommandHandler.TryExecuteAsync(prompt, this));

        RunHistory = new RunHistoryViewModel(
            sidebar,
            RetryHistoricalRun,
            ContinueHistoricalRun);

        _provider.Saved += OnProviderSaved;
        _provider.TestStarted += OnProviderTestStarted;
        _provider.TestCompleted += OnProviderTestCompleted;
        _sidebar.ProjectSelected += OnSidebarProjectSelected;
        _sidebar.ProjectAdded += OnSidebarProjectAdded;
        _conversationList.ConversationSelected += OnConversationSelected;
        _approvalViewModel.RequestPresented += OnApprovalPresented;
        _approvalViewModel.RequestResolved += OnApprovalResolved;

        // Sprint 0.5: the right-side Environment panel. Constructed after
        // _agentHost so it can attach to AgentHost.SubAgentRuns +
        // PendingAttachments.Attachments for live counts. The panel
        // also mirrors IBackgroundProcessSupervisor so the Background
        // Processes section is real (Wave 7 follow-up, plan §13 P0
        // risk "整个子进程树").
        _environmentPanel = new EnvironmentPanelViewModel(
            _workspace, processSupervisor, _agentHost, _sidebar, _clipboard, sourceRegistry);
        _environmentPanel.AttachTo();

        // Sidebar.SelectedProjectName → HasProject / Greeting /
        // SubGreeting forwarding now lives inside AppStatusViewModel
        // (it's the one that owns those derived properties), so
        // we don't need a local subscription here.
        RegisterCommandPaletteCommands();

        _ = RefreshAsync();
    }

    private async void OnSidebarProjectSelected(object? sender, ProjectSelectionChangedEventArgs args)
    {
        // AgentHost also subscribes to ProjectSelected to drive
        // the context-budget recompute + status message. The host
        // keeps the conversation list refresh here because the
        // sidebar / conversation VMs are its concern. The two
        // handlers are independent — both fire on the same event.
        // Wave 2: 先 reload sessions(sidebar 异步),再刷新 conversation list
        // / run history —— 它们都从 CurrentProjectSessions 读。
        try
        {
            await _sidebar.ReloadCurrentProjectSessionsAsync();
            _conversationList.Refresh(_sidebar.CurrentProject, _sidebar.CurrentProjectSessions);
            RunHistory.Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载项目失败: {ex.Message}";
        }
    }

    private void OnSidebarProjectAdded(object? sender, ProjectAddedEventArgs args)
    {
        // Wave 2: AddProjectAsync 已经把 current project 切到新加的;再
        // reload sessions 让 conversation list 跟 run history 跟上。
        OnSidebarProjectSelected(sender, new ProjectSelectionChangedEventArgs
        {
            Project = args.Project,
            StatusMessage = args.StatusMessage,
        });
    }

    private void OnConversationSelected(object? sender, ConversationSelectedEventArgs args)
    {
        _agentHost.ClearPreparedRunLink();
        // 1.0.1: drop the previous
        // conversation's in-flight run
        // state (plan steps / sub-agent
        // rows / status pill) so the
        // user switching to a different
        // conversation doesn't see the
        // old plan leaking above the
        // freshly-loaded activity feed.
        // IsRunning is intentionally
        // preserved — see the comment on
        // ClearRunState.
        _agentHost.ClearRunState();
        ActivityFeed.LoadConversation(args.Conversation);
        // 1.0.1: when the user re-clicks
        // the "new" placeholder card
        // (or lands on it after a refresh
        // with no sessions), the
        // conversation is null — the
        // same "I'm about to type"
        // intent as ⌘N. Fire the focus
        // event so the user lands in
        // the composer without an extra
        // click. We don't fire on
        // non-null conversation selects
        // (the user is reading old
        // context, not starting a new
        // task — auto-focus would steal
        // the keyboard and surprise
        // them).
        if (args.Conversation is null)
        {
            FocusComposerRequested?.Invoke(this, EventArgs.Empty);
        }
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
                () => { OpenSettings(); return Task.FromResult(true); }),
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
                    var result = await _projectPicker.PickProjectFolderAsync();
                    switch (result)
                    {
                        case PickerResult.Picked picked:
                            await AddProjectFromUiAsync(picked.Path);
                            break;
                        case PickerResult.Failed failed:
                            StatusMessage = failed.Reason;
                            break;
                        // Cancelled — stay silent.
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
                "打开运行记录",
                "浏览、筛选、重试或继续当前项目的历史运行",
                "",
                "M4 6 H20 M4 12 H20 M4 18 H14",
                () => { OpenRunHistory(); return Task.FromResult(true); }),
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
        // A new tool approval must take precedence over
        // every other modal — if a modal is up and the
        // user couldn't see the approval prompt, the
        // modal has to close. CloseAllModals() is the
        // single source of truth for the modal list
        // (keep it in sync with the escape handler in
        // MainWindow.axaml.cs — both are reviewed
        // together).
        CloseAllModals();
        _activeApprovalBubble = new ActivityItemViewModel(
            "需要确认",
            args.Request.Preview.Summary,
            "等待");
        ActivityFeed.Add(_activeApprovalBubble);
        StatusMessage = args.StatusMessage;
    }

    // Close every modal in one shot. Used by the Escape
    // handler in MainWindow.axaml.cs and by
    // OnApprovalPresented (which has to drop any modal
    // that might be hiding the approval prompt). The
    // ordered list mirrors the escape-handler's priority
    // order so both paths land in the same shape.
    public void CloseAllModals()
    {
        IsCommandPaletteOpen = false;
        IsSettingsOpen = false;
        IsMemoryEditorOpen = false;
        IsGitStatusOpen = false;
        IsRunHistoryOpen = false;
        IsShortcutsOpen = false;
        IsPluginsOpen = false;
        IsScheduledOpen = false;
        IsSitesOpen = false;
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

    private void RetryHistoricalRun(RunHistoryItemViewModel item)
    {
        _conversationList.SelectConversation(item.ConversationId);
        IsRunHistoryOpen = false;
        _agentHost.RetryRun(item.Run);
    }

    private void ContinueHistoricalRun(RunHistoryItemViewModel item)
    {
        _conversationList.SelectConversation(item.ConversationId);
        IsRunHistoryOpen = false;
        _agentHost.PrepareContinuation(item.Run);
    }

    // 1.0.1: "→ composer" affordance on the
    // RunHistory 详情 Goal. The user is
    // looking at a finished run, decides
    // "this goal is good but I want to
    // tweak it before re-sending" — the
    // daily-driver flow that ⌘C + manual
    // paste into the composer (3 steps)
    // is one click. Sibling of the
    // user-bubble Edit affordance in
    // MainWindow.axaml.cs, just sourced
    // from a historical run instead of
    // the current conversation.
    //
    // The 4 lines after the DraftPrompt
    // set are the same pattern
    // NewStandaloneConversationAsync
    // uses: close the source modal
    // (RunHistory), raise
    // FocusComposerRequested so the
    // user lands on the composer, the
    // same hook as ⌘N. The user
    // lands with the cursor in the
    // goal text, ready to edit.
    public void CopyRunGoalToComposer(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return;
        }
        IsRunHistoryOpen = false;
        _agentHost.DraftPrompt = goal;
        FocusComposerRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync()
    {
        // 2026-08-03: show a one-time warning if a previous run crashed
        // (i.e. the CrashReporter append-only log grew since the last
        // call to TryGetLastCrashSinceLastSeen). The toast is best-
        // effort: if it fails, the user can still read crash.log
        // directly from the path Toast points at.
        var crashSummary = CrashReporter.TryGetLastCrashSinceLastSeen();
        if (!string.IsNullOrWhiteSpace(crashSummary))
        {
            _toast.Show(
                $"上轮异常退出 ({crashSummary}) — 详情 {CrashReporter.LogPath}",
                ToastLevel.Warning);
        }

        // Wave 3: refresh Standalone sessions alongside the project tree
        // and conversations. The Standalone list is global (not project-
        // scoped), so loading it here once at startup + on F5 is the
        // cheapest correct place to wire it.
        await RefreshStandaloneConversationsAsync();
        // Same RelayCommand-exception-escape risk as SendTaskAsync
        // (d7b179c): F5 (KeyBinding) and the palette both invoke
        // RefreshCommand directly with no SafeRun wrapper. The body
        // touches settings + projects + JSON normalization; any of
        // them can throw (corrupt file, permission denied, removed        // drive). Catch and surface to the status bar so the user
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
            // 2026-08-05: cap max output tokens at
            // 16K to match the MiniMax M3 platform
            // limit. The previous 32768 was a
            // historical default from when the
            // catalog was Anthropic-skewed; M3's
            // published max output is 16K and
            // M3-highspeed matches. A user with
            // 32K configured silently lost the
            // last 16K of any long-form response
            // (truncated mid-sentence). The clamp
            // is conservative: a future model
            // with a higher cap will need its
            // own model-specific ceiling here.
            _settings.MaxOutputTokens = Math.Clamp(_settings.MaxOutputTokens, 256, 16384);
            _settings.ConversationContextRatio = Math.Clamp(_settings.ConversationContextRatio, 0.3, 1.0);
            ToolSettingsService.Normalize(_settings, _toolRegistry);

            // Sprint 0.5: restore the 2-toggle permission state +
            // Environment panel visibility. The two permission toggles
            // are the source of truth in _settings; NoWriteMode is a
            // derived convenience for the existing agent-host bridge
            // and the page-header pill.
            EnvironmentPanelOpen = _settings.EnvironmentPanelOpen;
            DefaultAccess = _settings.DefaultAccess;
            FullAccessEnabled = _settings.FullAccessEnabled;
            // NoWriteMode = !DefaultAccess so the existing ⌘⇧R
            // shortcut (which flips NoWriteMode) stays meaningful.
            NoWriteMode = !DefaultAccess;

            var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
            var active = ProviderSettingsService.GetSelectedProvider(_settings);

            _appStatus.ActiveProvider = active is null ? "未配置模型" : active.Name;
            _appStatus.ActiveModel = active is null ? "配置模型后即可运行任务" : active.SelectedModelId;
            _appStatus.Readiness = active is not null && !string.IsNullOrWhiteSpace(active.ApiKey) ? "可运行" : "需要密钥";

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

            _sidebar.Refresh(workspaces);
            // Wave 2: reload sessions,再让 conversation list / run history 跟上
            await _sidebar.ReloadCurrentProjectSessionsAsync();
            // Restore the last-active conversation if its id still
            // matches a conversation on the current project.
            // ConversationListViewModel.Refresh already handles the
            // "preferred id not found" case by falling back to the most
            // recent conversation / "new" placeholder, so a stale id
            // from a deleted conversation degrades silently rather
            // than throwing.
            _conversationList.Refresh(_sidebar.CurrentProject, _sidebar.CurrentProjectSessions, _settings.LastActiveConversationId);
            RunHistory.Refresh();
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

            StatusMessage = AppRuntimeProfile.IsIsolated
                ? "已加载（隔离会话：不读取系统钥匙串）。"
                : "已加载。";
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

        _appStatus.ActiveProvider = args.ProviderName;
        _appStatus.ActiveModel = args.ModelId;
        _appStatus.Readiness = "可运行";
        StatusMessage = args.WarningMessage ??
                        (args.AlreadyExisted ? "已更新模型配置。" : "已保存模型配置。");
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
        _appStatus.IsProviderTesting = true;
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
        _appStatus.IsProviderTesting = false;
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

        _appStatus.Readiness = args.IsSuccess ? "可运行" : "需检查";
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
