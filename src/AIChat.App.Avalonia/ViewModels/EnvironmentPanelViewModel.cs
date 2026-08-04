using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Sources;
using AIChat.Application.Workspace;
using AIChat.App.Avalonia.Composition;
using AIChat.Domain.BackgroundProcesses;
using AIChat.Domain.Projects;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// One row in the right-side "交付物" section. A deliverable is any file
// in the project's art/ directory — typically a generated mockup,
// screenshot, or rendered image. We list them by mtime desc so the most
// recent thing the agent (or the user) shipped is at the top, matching
// the Mavis / Codex "what did this run actually produce?" surface.
public sealed class DeliverableViewModel
{
    public string Name { get; }
    public string DisplaySize { get; }
    public DateTimeOffset ModifiedAt { get; }
    public string ModifiedDisplay { get; }
    public string FullPath { get; }

    public DeliverableViewModel(string name, string fullPath, long sizeBytes, DateTimeOffset modifiedAt)
    {
        Name = name;
        FullPath = fullPath;
        ModifiedAt = modifiedAt;
        ModifiedDisplay = FormatRelative(modifiedAt);
        DisplaySize = FormatSize(sizeBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalSeconds < 60) return "刚刚";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} 分钟前";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} 小时前";
        return when.LocalDateTime.ToString("MM-dd HH:mm");
    }
}

// One small color dot in the 子智能体 section. Codex shows up to 4
// dots (explorer / coder / reviewer / researcher) with a fixed palette
// so the visual stays stable. ColorBrush is an Avalonia IBrush so the
// XAML can bind it directly to Ellipse.Fill without a converter.
public sealed class SubAgentTemplateDotViewModel
{
    public string Label { get; }
    public IBrush ColorBrush { get; }

    public SubAgentTemplateDotViewModel(string label, IBrush colorBrush)
    {
        Label = label;
        ColorBrush = colorBrush;
    }
}

// One row in the Environment panel's "Background Processes"
// section. The row is a thin view over the supervisor's domain
// BackgroundProcess (the supervisor owns persistence, PID, log
// tail, and the kill path — see BackgroundProcessSupervisor).
// The VM exposes the display fields the XAML binds plus a
// StopCommand that the panel can wire to a "Stop" button.
//
// Wave 7 follow-up (plan §13 P0 risk "整个子进程树"): the row
// stops the process tree, not just the immediate child. The
// supervisor sends SIGTERM to the process group, escalates to
// SIGKILL after killTimeout, and the row's StatusLabel turns
// into "已强制停止" so the user can see the kill happened
// non-quietly.
public sealed class BackgroundProcessViewModel
{
    public string Id { get; }
    public string DisplayName { get; }
    public string StatusLabel { get; }
    public string PidLabel { get; }
    public string CommandLine { get; }
    public bool IsRunning { get; }
    public IBrush StatusBrush { get; }
    // 2026-08-03: the supervisor captures stdout + stderr into a
    // ring buffer (MaxLogLines, defined on the supervisor). Showing
    // the tail lets a Sites preview user see why the static server
    // crashed; the toggle / copy are the user affordances the
    // half-shipping state was missing.
    public string LogTail { get; }
    public IReadOnlyList<string> LogTailLines { get; }
    public bool HasLog => LogTailLines.Count > 0;
    public string CopyLogTooltip => HasLog
        ? "复制日志到剪贴板"
        : "暂无日志输出";

    // StopCommand is a small closure over the supervisor + this row's
    // id. We construct it once per row so the XAML can bind
    // Command="{Binding StopCommand}" without a code-behind lookup;
    // when the row is re-mirrored on the next Changed event, the
    // command instance is replaced — the per-row button never
    // references a stale id.
    public IAsyncRelayCommand StopCommand { get; }
    public IRelayCommand CopyLogCommand { get; }

    public BackgroundProcessViewModel(
        BackgroundProcess process,
        IBackgroundProcessSupervisor supervisor,
        IClipboardService? clipboard = null)
    {
        Id = process.Id;
        DisplayName = string.IsNullOrWhiteSpace(process.Name) ? process.Command : process.Name;
        StatusLabel = FormatStatus(process.Status);
        PidLabel = process.Pid > 0 ? $"PID {process.Pid}" : "";
        CommandLine = BuildCommandLine(process);
        IsRunning = process.Status == BackgroundProcessStatus.Running;
        StatusBrush = StatusToBrush(process.Status);
        LogTailLines = (IReadOnlyList<string>)(process.LogTail?.ToList() ?? new List<string>());
        LogTail = string.Join("\n", LogTailLines);
        // Capture the id in the closure so the XAML's button
        // click always targets the right process, even after the
        // row is replaced on the next Changed event.
        var capturedId = process.Id;
        StopCommand = new AsyncRelayCommand(
            () => supervisor.StopAsync(capturedId),
            () => IsRunning);
        // The clipboard service is optional so unit tests that
        // construct this view-model directly do not need a
        // TopLevel. When null, CopyLogCommand is a no-op that
        // swallows the click (the button is hidden by HasLog in
        // the XAML anyway, so the no-op path is rare).
        CopyLogCommand = new RelayCommand(
            async () =>
            {
                if (clipboard is null || !HasLog)
                {
                    return;
                }
                try
                {
                    await clipboard.SetTextAsync(LogTail);
                }
                catch
                {
                    // Clipboard may be unavailable in headless tests
                    // or when no TopLevel has been set. The user
                    // can still read the log inline in the panel.
                }
            });
    }

    private static string BuildCommandLine(BackgroundProcess process)
    {
        if (process.Arguments.Count == 0) return process.Command;
        return process.Command + " " + string.Join(" ", process.Arguments);
    }

    private static string FormatStatus(BackgroundProcessStatus status) => status switch
    {
        BackgroundProcessStatus.Running => "运行中",
        BackgroundProcessStatus.Stopped => "已停止",
        BackgroundProcessStatus.Crashed => "已崩溃",
        BackgroundProcessStatus.ForceKilled => "已强制停止",
        _ => "未启动",
    };

    // Same fixed palette as the sub-agent section: green for healthy,
    // red for failure, amber for force-kill, grey for idle. The user
    // can scan the panel and read state from the dot color alone
    // without having to read the label.
    private static IBrush StatusToBrush(BackgroundProcessStatus status) => status switch
    {
        BackgroundProcessStatus.Running => new SolidColorBrush(Color.Parse("#5cd6a8")),
        BackgroundProcessStatus.Stopped => new SolidColorBrush(Color.Parse("#9aa0a6")),
        BackgroundProcessStatus.Crashed => new SolidColorBrush(Color.Parse("#ff6b6b")),
        BackgroundProcessStatus.ForceKilled => new SolidColorBrush(Color.Parse("#f5a623")),
        _ => new SolidColorBrush(Color.Parse("#9aa0a6")),
    };
}

// One row in the right-side "来源" (sources) section. Wave 7 ships
// with a single source kind — pasted images that the user added via
// ⌘V — and uses this record as the unified display shape so future
// source kinds (web search, connector, plugin) can plug in without
// changing the XAML. Kind is a free-form string ("image" / "file" /
// "web" / "plugin") so the XAML can drive the icon glyph off it.
public sealed partial class SourceRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Kind { get; }
    public string DisplayName { get; }
    public string? Detail { get; }

    // 1.0.1: full backend Source reference so the
    // EnvironmentPanelView XAML Click handler can
    // raise the panel's InsertReferenceRequested
    // event with the row's Source payload. The
    // previous design had InsertReferenceCommand
    // + OnInsertRequested as a fallback bridge
    // (in case the XAML's Click wiring was lost),
    // but the path is fully wired now and the
    // fallback would only get exercised if
    // EnvironmentPanelView.axaml.cs was deleted
    // — dead code, removed.
    public AIChat.Domain.Sources.Source? Source { get; set; }

    // 1.0.1: the @-reference text a daily-driver
    // user would type to cite this Source in a
    // prompt by hand (the "引用" button writes it
    // for them, but a user who knows what they
    // want may prefer to type "@web:abc" rather
    // than reach for the sidebar). The Sources
    // list used to show only the display name
    // + URL/capture-time — the id was hidden
    // behind the panel, so a user who wanted to
    // type the reference had to open the
    // settings.json file (or guess). Now it
    // shows the @-reference text right under the
    // filename in muted mono so the user can
    // read it at a glance and copy it. Empty for
    // pending attachment rows (no Source.Id to
    // reference — the pending row's id is an
    // internal "pending:<filename>" placeholder
    // and not a valid @-reference).
    public string Reference => Source is null
        ? ""
        : AIChat.Application.Sources.SourceReferenceParser.FormatReference(Source);

    // 1.0.1: inline-expand state for the per-row
    // detail panel. The Sources list's per-row
    // chevron button toggles this so the user can
    // see the full captured body without leaving
    // the side panel (the row's display name is
    // truncated to 60 chars in the list and the
    // full Source.Content lives behind a click).
    // Each row holds its own state so two rows
    // can be expanded at once — useful for
    // comparing a clipboard snapshot against a
    // web fetch.
    [ObservableProperty]
    private bool isExpanded;

    // The full body of the captured Source, or
    // null when the row is a pending attachment
    // (no Source to show — pending attachments
    // own a different file-payload path and have
    // no readable text to inline). The XAML
    // binds Visibility to "Content != null" so
    // the expand panel just doesn't render for
    // pending rows.
    public string? DetailContent => Source?.Content;

    public SourceRowViewModel(
        string id,
        string kind,
        string displayName,
        string? detail = null)
    {
        Id = id;
        Kind = kind;
        DisplayName = displayName;
        Detail = detail;
    }

    // Back-compat shim for the pending-attachment
    // rows that don't have a Source.Id — synthesise
    // a stable id from the file name so the XAML
    // can still bind without nullability noise.
    public static SourceRowViewModel ForPendingAttachment(string fileName, string? detail = null) =>
        new($"pending:{fileName}", "image", fileName, detail);
}

// Sprint 0.5: right-side Environment panel ViewModel (plan §4 / §7 Wave 5).
// Reads existing data (git changes via IWorkspaceChangeService, sub-agent
// runs from AgentHost, pending attachments from AgentHost, background
// processes from IBackgroundProcessSupervisor) — does NOT own any new
// domain model.
public sealed partial class EnvironmentPanelViewModel : ViewModelBase
{
    private readonly IWorkspaceChangeService _workspace;
    private readonly IBackgroundProcessSupervisor _processSupervisor;
    private readonly AgentHostViewModel _agentHost;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly IClipboardService _clipboard;
    private readonly ISourceRegistry _sourceRegistry;

    [ObservableProperty]
    private string branchName = "(未选择项目)";

    [ObservableProperty]
    private int changeAdded;

    [ObservableProperty]
    private int changeRemoved;

    [ObservableProperty]
    private int subAgentTotal;

    // Codex parity: "N 完成" total. Currently same as SubAgentTotal
    // because we don't have a per-state sub-agent counter yet; the
    // field is exposed separately so Wave 7 can wire a real "failed /
    // budgeted" split without breaking the XAML binding.
    [ObservableProperty]
    private int subAgentCompleted;

    // Codex parity: the 4 small color dots in the 子智能体 section.
    // Driven by SubAgentTemplateDots; refreshed on every recount.
    public ObservableCollection<SubAgentTemplateDotViewModel> SubAgentTemplateDots { get; } = [];

    // Per-run list under the 子智能体 section. Mirrors
    // AgentHost.SubAgentRuns by reference (same SubAgentRunViewModel
    // instance), sorted newest-first so the user can read the most
    // recent dispatch at the top. The XAML's DataTemplate binds to
    // Status / DurationDisplay / Task / TemplateDisplay, and Avalonia
    // re-evaluates the bindings on the live instance when the harness
    // emits SubAgentStarted → SubAgentCompleted, so the per-row UI
    // ticks from "运行中…" to "12s" without the panel needing its own
    // PropertyChanged fan-out. Newest-first ordering is rebuilt in
    // RecountSubAgents (cheap; n ≤ 10 in practice).
    public ObservableCollection<SubAgentRunViewModel> SubAgentRuns { get; } = [];

    // Wave 7 follow-up (plan §13 P0 risk "整个子进程树"):
    // Background Processes list, now real. Each row is a thin
    // view over the supervisor's domain process — see
    // BackgroundProcessViewModel. The panel re-mirrors the
    // supervisor's state on every Changed event so the user sees
    // running / stopped / crashed transitions without a manual
    // refresh click.
    //
    // The plan §7.7 rule ("supervisor 未建前不得展示入口") is
    // now satisfied — the supervisor is built and registered in
    // DI, so ShowBackgroundProcesses defaults to true and the
    // section is visible by default. If the supervisor is ever
    // absent (e.g. a headless test host substitutes a fake), the
    // section still renders the empty-state hint and a real
    // Changed event is never required for that path.
    public ObservableCollection<BackgroundProcessViewModel> BackgroundProcesses { get; } = [];

    [ObservableProperty]
    private bool hasBackgroundProcesses;

    [ObservableProperty]
    private bool showBackgroundProcesses = true;

    // Computed: SubAgentTotal > 0. Avalonia's ObjectConverters doesn't ship
    // a GreaterThan in every version, so we derive it here and let XAML
    // bind directly. Same shape as AgentHost.HasSubAgentRuns.
    public bool HasSubAgents => SubAgentTotal > 0;
    partial void OnSubAgentTotalChanged(int value) => OnPropertyChanged(nameof(HasSubAgents));

    [ObservableProperty]
    private int subAgentRunning;

    [ObservableProperty]
    private int subAgentFailed;

    [ObservableProperty]
    private int sourceCount;

    [ObservableProperty]
    private string sourceSummary = "暂无";

    // Per-source list. Wave 7 ships with the image-attachment
    // surface — the user's pasted ⌘V images that will travel with
    // the next message. The XAML binds ItemsControl to this
    // collection and shows the file name + an attachment glyph
    // so the user can see "this run will include N images" from
    // the right rail.
    //
    // Web search / clipboard / connector / plugin sources remain
    // future work; the rule from plan §7.7 is the same as for
    // Background Processes: don't show an entry that doesn't have
    // a real source behind it. The "查看全部" button below the
    // list is the only placeholder that ships in this slice.
    public ObservableCollection<SourceRowViewModel> Sources { get; } = [];

    // 1.0.1: raised by the per-row "引用" button on a
    // Source row. The handler is MainWindow.xaml.cs,
    // which reads PromptInput.CaretIndex at click
    // time and forwards the Source to
    // AgentHostViewModel.InsertSourceReferenceAtCaret.
    // The event is the bridge between the side panel
    // and the composer — both live in MainWindow.axaml
    // but the per-row command binding on a Source row
    // can't see the composer because commands run in
    // the row's DataContext (the panel VM), not the
    // MainWindowViewModel that owns the composer
    // TextBox.
    public event Action<AIChat.Domain.Sources.Source>? InsertReferenceRequested;

    // 1.0.1: the "提交或推送" button in the
    // 本地 section of the panel was a stub
    // pointing at "Wave 6 实现". Wave 6
    // actually shipped the full Git status
    // modal (with stage / commit / restore
    // / push affordances) — but the button
    // is in this panel, not the modal. The
    // cleanest way to surface the modal
    // from here is the same event pattern
    // InsertReferenceRequested uses:
    // EnvironmentPanelView's code-behind
    // raises the event, MainWindow
    // subscribes and calls its
    // OpenGitStatusCommand. The user
    // gets a single click from
    // Environment panel → Git modal →
    // commit. The push / PR work itself
    // is still future work (no GitHub
    // OAuth yet), but the entry point no
    // longer lies.
    public event Action? OpenGitStatusRequested;

    // 1.0.1: public raise helper so the XAML
    // click handler (EnvironmentPanelView.axaml.cs)
    // can fire InsertReferenceRequested without
    // taking a direct dependency on the
    // EnvironmentPanelViewModel-internalised
    // RelayCommand path. The button binds the
    // Click event and the row's Tag, so the
    // code-behind is the natural bridge.
    public void RaiseInsertReferenceRequested(AIChat.Domain.Sources.Source source)
    {
        InsertReferenceRequested?.Invoke(source);
    }

    public void RaiseOpenGitStatusRequested()
    {
        OpenGitStatusRequested?.Invoke();
    }

    [ObservableProperty]
    private string lastRefreshDisplay = "尚未刷新";

    // True when no project is selected — we hide git details and show a hint
    // instead of fabricating "(no changes)".
    [ObservableProperty]
    private bool isProjectRequired = true;

    // "进度" section. Mirrors the Mavis / Codex "what is the agent doing
    // right now / what did it just finish" line that lives next to the
    // session id. Empty string when the host hasn't started a run yet.
    [ObservableProperty]
    private string currentTaskTitle = "";

    [ObservableProperty]
    private string lastRunSummary = "";

    // 1.0.1: per-state text color for
    // LastRunSummary. The previous
    // shape was a single
    // Text2Brush — a user glancing
    // at the 进度 section couldn't
    // tell "上次运行: 失败" from
    // "上次运行: 完成" without
    // reading the text. Mirrors the
    // SubAgent / RunHistory palette
    // so the same shade of red means
    // the same thing across all 3
    // surfaces.
    [ObservableProperty]
    private IBrush lastRunSummaryBrush = Brush.Parse("#9aa0a6");

    [ObservableProperty]
    private bool isTaskRunning;

    // "交付物" section. Files under the project's art/ directory, newest
    // first. Populated by RefreshAsync so the user can see "this run
    // produced N images" without digging into the agent's tool calls.
    public ObservableCollection<DeliverableViewModel> Deliverables { get; } = [];

    [ObservableProperty]
    private string deliverablesDirectory = "art";

    [ObservableProperty]
    private bool hasDeliverables;

    partial void OnIsTaskRunningChanged(bool value) => RecomputeProgressText();
    partial void OnCurrentTaskTitleChanged(string value) => RecomputeProgressText();

    private void RecomputeProgressText()
    {
        if (IsTaskRunning)
        {
            LastRunSummary = string.IsNullOrWhiteSpace(CurrentTaskTitle)
                ? "正在运行…"
                : $"正在跑：{CurrentTaskTitle}";
            LastRunSummaryBrush = Brush.Parse("#f5a623"); // amber — matches the in-flight / cancelled palette
        }
    }

    // Subscribed to AgentHost.SubAgentRuns to keep counts fresh. Hooked in
    // AttachTo(); cleared in DetachFrom() so the view-model can be reused
    // across re-attaches without leaking events.
    private ObservableCollection<SubAgentRunViewModel>? _attachedSubAgentRuns;
    private ObservableCollection<PendingAttachmentViewModel>? _attachedAttachments;
    // 1.0.1: 1Hz timer to live-update
    // running sub-agent DurationDisplay
    // values. See constructor for
    // lifecycle. The VM lives for the
    // app's lifetime so we don't need
    // to dispose the timer.
    private readonly DispatcherTimer _subAgentDurationTimer;
    // PropertyChanged hook on AgentHost. The panel needs to surface the
    // "is a task running / what was it" state in its 进度 section, but
    // AgentHost is a peer ViewModelBase — pulling it through a Func
    // would just be a re-implementation of PropertyChanged. Subscribe
    // once on Attach, unsubscribe on Detach, same pattern as the
    // collection subscriptions above.
    private AgentHostViewModel? _attachedAgentHost;
    // Reference to the supervisor's Changed subscription so DetachFrom
    // can drop it. The supervisor fires Changed on whatever thread the
    // mutation happened on (background thread for process Exited), so
    // the handler marshals to the UI thread before touching the
    // ObservableCollection.
    private IBackgroundProcessSupervisor? _attachedSupervisor;

    public EnvironmentPanelViewModel(
        IWorkspaceChangeService workspace,
        IBackgroundProcessSupervisor processSupervisor,
        AgentHostViewModel agentHost,
        ProjectSidebarViewModel sidebar,
        IClipboardService clipboard,
        ISourceRegistry sourceRegistry)
    {
        _workspace = workspace;
        _processSupervisor = processSupervisor;
        _agentHost = agentHost;
        _sidebar = sidebar;
        _clipboard = clipboard;
        _sourceRegistry = sourceRegistry;
        _sourceRegistry.Changed += (_, _) => RefreshSources();
        // Eager ReloadAsync — the first Sources section
        // paint should already have the persisted
        // captures, not "(loading…)".
        _ = _sourceRegistry.ReloadAsync().ContinueWith(_ => RefreshSources(),
            TaskScheduler.Default);
        // ObservableCollection mutations don't fire PropertyChanged on
        // derived bools; wire CollectionChanged once here so the
        // IsVisible binding on the XAML section collapses cleanly when
        // the directory is empty or missing. (We don't need to
        // unsubscribe anywhere — this VM's lifetime is the app's.)
        Deliverables.CollectionChanged += (_, _) => HasDeliverables = Deliverables.Count > 0;
        // BackgroundProcesses is a re-mirror of the supervisor's
        // snapshot — the count changes on every Changed event. Same
        // pattern as Deliverables: keep HasBackgroundProcesses in
        // lock-step so the XAML's empty-state hint flips without a
        // manual OnPropertyChanged.
        BackgroundProcesses.CollectionChanged += (_, _) => HasBackgroundProcesses = BackgroundProcesses.Count > 0;
        // 1.0.1: 1Hz timer to live-update
        // the per-row DurationDisplay for
        // running sub-agents. Without
        // this, a 30-second running
        // sub-agent just says "运行中…"
        // the whole time — the user
        // can't tell if it's making
        // progress or stuck. The tick
        // walks SubAgentRuns (cheap —
        // usually 0-10 rows) and forces
        // a DurationDisplay refresh on
        // rows whose status is still
        // "Running". Completed/failed
        // rows are skipped — their
        // duration is final.
        _subAgentDurationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _subAgentDurationTimer.Tick += (_, _) => TickSubAgentDurations();
        _subAgentDurationTimer.Start();
    }

    // 1.0.1: per-tick refresh of running
    // sub-agent rows' DurationDisplay.
    // DispatcherTimer.Tick fires on the
    // UI thread, so the assignments are
    // safe to make directly without
    // dispatcher.Post.
    private void TickSubAgentDurations()
    {
        foreach (var row in SubAgentRuns)
        {
            if (row.IsRunning)
            {
                // Re-format with the
                // current DateTimeOffset.Now
                // (since the row's
                // StartedAt is fixed, the
                // only thing that changed
                // is the wall clock).
                // FormatDuration returns
                // "运行中…" for null
                // CompletedAt — we want
                // the actual elapsed
                // time string here, so
                // we set CompletedAt to
                // null (it already is)
                // and call the helper
                // directly via a public
                // re-format.
                row.RefreshRunningDuration();
            }
        }
    }

    // Called by the host after construction. The host owns the lifetime of
    // the sub-agent / attachment collections; we just watch them.
    public void AttachTo()
    {
        DetachFrom();

        _attachedSubAgentRuns = _agentHost.SubAgentRuns;
        _attachedSubAgentRuns.CollectionChanged += OnSubAgentRunsChanged;
        RecountSubAgents();

        _attachedAttachments = _agentHost.PendingAttachments.Attachments;
        _attachedAttachments.CollectionChanged += OnAttachmentsChanged;
        RecountSources();

        // Mirror the 进度 section off AgentHost's run-state fields. The
        // CurrentTaskTitle is the prompt text — captured at SendTask and
        // cleared when the run ends (the host exposes that via the
        // LastAssistantStatus flip; we treat any non-empty status as
        // "task done, no longer running").
        _attachedAgentHost = _agentHost;
        _attachedAgentHost.PropertyChanged += OnAgentHostPropertyChanged;
        SyncRunState();

        // Wire the background-process supervisor. RefreshAsync is
        // called eagerly so the user sees restart-recovery state
        // (e.g. "已崩溃" for processes that died while the app
        // was off) the moment the panel mounts.
        _attachedSupervisor = _processSupervisor;
        _attachedSupervisor.Changed += OnSupervisorChanged;
        SyncBackgroundProcesses();
    }

    public void DetachFrom()
    {
        if (_attachedSubAgentRuns is not null)
        {
            _attachedSubAgentRuns.CollectionChanged -= OnSubAgentRunsChanged;
            _attachedSubAgentRuns = null;
        }

        if (_attachedAttachments is not null)
        {
            _attachedAttachments.CollectionChanged -= OnAttachmentsChanged;
            _attachedAttachments = null;
        }

        if (_attachedAgentHost is not null)
        {
            _attachedAgentHost.PropertyChanged -= OnAgentHostPropertyChanged;
            _attachedAgentHost = null;
        }

        if (_attachedSupervisor is not null)
        {
            _attachedSupervisor.Changed -= OnSupervisorChanged;
            _attachedSupervisor = null;
        }
    }

    private void OnSubAgentRunsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RecountSubAgents();

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RecountSources();

    private void OnAgentHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentHostViewModel.IsRunning)
            or nameof(AgentHostViewModel.LastAssistantStatus))
        {
            SyncRunState();
        }
    }

    // The supervisor fires Changed on whatever thread the mutation
    // happened on — the Process.Exited event runs on a background
    // thread, so a process death while the user is reading the panel
    // will arrive off-UI. Marshal back to the dispatcher before
    // touching the ObservableCollection, otherwise Avalonia throws
    // "Collection was modified during enumeration" on the next
    // ItemsControl re-bind.
    private void OnSupervisorChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SyncBackgroundProcesses();
        }
        else
        {
            Dispatcher.UIThread.Post(SyncBackgroundProcesses);
        }
    }

    private void SyncRunState()
    {
        if (_attachedAgentHost is null) return;
        IsTaskRunning = _attachedAgentHost.IsRunning;
        // While running, show the prompt text. Once finished, fall back
        // to a "completed / failed" status line so the section never
        // collapses to a single "…" between runs.
        if (IsTaskRunning)
        {
            CurrentTaskTitle = _attachedAgentHost.DraftPrompt.Length > 0
                ? TruncateForPanel(_attachedAgentHost.DraftPrompt)
                : "";
        }
        else
        {
            CurrentTaskTitle = "";
            var status = _attachedAgentHost.LastAssistantStatus;
            LastRunSummary = string.IsNullOrEmpty(status)
                ? ""
                : $"上次运行：{status}";
            // 1.0.1: pick the text color
            // based on the last status.
            // Mirrors the SubAgent /
            // RunHistory palette so
            // a glance at the 进度
            // section tells the user
            // "the last run failed" the
            // same way the run history
            // list does. Empty status
            // → default muted gray.
            LastRunSummaryBrush = status switch
            {
                "完成" => Brush.Parse("#5cd6a8"),
                "失败" => Brush.Parse("#ff6b6b"),
                "已停止" => Brush.Parse("#f5a623"),
                "预算暂停" => Brush.Parse("#9aa0a6"),
                _ => Brush.Parse("#9aa0a6"),
            };
        }
    }

    private static string TruncateForPanel(string text)
    {
        const int max = 48;
        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }

    private void RecountSubAgents()
    {
        var runs = _agentHost.SubAgentRuns;
        SubAgentTotal = runs.Count;
        SubAgentRunning = runs.Count(r => r.IsRunning);
        SubAgentFailed = runs.Count(r => r.IsFailed);
        SubAgentCompleted = runs.Count(r => r.IsCompleted);
        RefreshSubAgentDots(runs);
        SyncSubAgentRuns(runs);
    }

    // Mirror AgentHost.SubAgentRuns into our own ObservableCollection
    // so the XAML can bind ItemsControl to it. We re-add the same
    // instance references (not new VMs) so per-row PropertyChanged
    // (Status: Running → Completed, DurationDisplay: "运行中…" → "12s")
    // continues to flow through to the existing DataTemplate binding
    // without a per-row subscriber. Newest-first ordering matches
    // Codex's per-run list (most recent dispatch at the top).
    private void SyncSubAgentRuns(IReadOnlyList<SubAgentRunViewModel> runs)
    {
        SubAgentRuns.Clear();
        if (runs.Count == 0)
        {
            return;
        }

        // OrderByDescending is a small allocation but n is bounded
        // (the harness dispatches a handful of sub-agents per top-level
        // run, and the list is cleared at the start of each new
        // SendTaskCommand). Re-adding the same instances means we
        // don't have to wire per-item PropertyChanged subscriptions.
        var ordered = runs.OrderByDescending(r => r.StartedAt).ToList();
        foreach (var run in ordered)
        {
            SubAgentRuns.Add(run);
        }
    }

    // Codex parity: render up to 4 small color dots, one per template
    // that has at least one run. The palette is fixed (blue / amber /
    // pink / green) so the user gets the same visual across sessions;
    // later slices (Wave 7) may map templates to specific colors.
    private void RefreshSubAgentDots(IReadOnlyList<SubAgentRunViewModel> runs)
    {
        SubAgentTemplateDots.Clear();
        if (runs.Count == 0)
        {
            return;
        }

        // One dot per distinct template that has any run. Cap at 4 to
        // match Codex's visual; if more templates show up, the
        // overflow becomes a "..." suffix in a later slice.
        var templates = runs.Select(r => r.TemplateDisplay ?? "agent")
            .Distinct()
            .Take(4)
            .ToList();

        var palette = new[]
        {
            new SolidColorBrush(Color.Parse("#5eb1ff")),
            new SolidColorBrush(Color.Parse("#f5a623")),
            new SolidColorBrush(Color.Parse("#ff6b9d")),
            new SolidColorBrush(Color.Parse("#5cd6a8"))
        };

        for (var i = 0; i < templates.Count; i++)
        {
            SubAgentTemplateDots.Add(new SubAgentTemplateDotViewModel(
                templates[i],
                palette[i % palette.Length]));
        }
    }

    private void RecountSources()
    {
        // The Changed event handler + the ctor's
        // fire-and-forget ReloadAsync().ContinueWith
        // both call in from different threads:
        // the Changed handler fires on whatever
        // thread the registry mutation happened
        // (the click handler thread for the
        // Sources panel's add/remove buttons),
        // while the ctor's ContinueWith runs on
        // TaskScheduler.Default's thread pool.
        // ObservableCollection is not thread-safe,
        // so the mutation has to land on the UI
        // thread or Avalonia throws on the next
        // ItemsControl re-bind ("Collection was
        // modified during enumeration"). Headless
        // test hosts don't own a live
        // Application.Current — same fall-through
        // ToolApprovalViewModel.RunOnUiThread uses,
        // so the test suite can drive the panel
        // from a non-UI thread without the
        // Post landing on a never-pumped
        // dispatcher.
        if (global::Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            DoRecountSources();
        }
        else
        {
            Dispatcher.UIThread.Post(DoRecountSources);
        }
    }

    private void DoRecountSources()
    {
        //   1. Pending image attachments (the
        //      composer's ⌘V + drag-drop strip) — these
        //      travel with the NEXT message, not
        //      persisted across restarts.
        //   2. The Source registry (clipboard
        //      snapshots, future web/connector
        //      imports) — persisted, available to
        //      the user across sessions.
        // The Sources list shows the registry's
        // contents; the count line includes both so
        // the user sees the "ready to send" image
        // attachments and the persisted captures in
        // one number.
        var attachments = _agentHost.PendingAttachments.Attachments;
        var persisted = _sourceRegistry.Sources;
        SourceCount = attachments.Count + persisted.Count;
        // 1.0.1: the previous "X 个待发送"
        // label conflated the two
        // populations — the pending
        // image attachments really are
        // waiting to be sent, but the
        // persisted Sources (clipboard
        // snapshots, web fetches) are
        // already saved. A user with 3
        // persisted Sources + 0 pending
        // attachments used to read
        // "3 个待发送" and wonder why
        // nothing was in the composer.
        // The label now reports each
        // population separately so the
        // number matches the user's
        // mental model.
        if (SourceCount == 0)
        {
            SourceSummary = "暂无";
        }
        else if (attachments.Count == 0)
        {
            SourceSummary = $"{persisted.Count} 个已保存";
        }
        else if (persisted.Count == 0)
        {
            SourceSummary = $"{attachments.Count} 个待发送";
        }
        else
        {
            SourceSummary = $"{attachments.Count} 个待发送 / {persisted.Count} 个已保存";
        }
        // Mirror the real pending attachments into the per-source
        // list so the right rail reflects the same state as the
        // attachment strip above the composer. Each row gets a
        // "image" kind so the XAML can drive the icon glyph off
        // the kind string instead of hardcoding a clipboard icon.
        // Future source kinds (web search, connector) plug in
        // through the same source — see SourceRowViewModel docs.
        Sources.Clear();
        foreach (var attachment in attachments)
        {
            Sources.Add(SourceRowViewModel.ForPendingAttachment(
                fileName: attachment.FileName,
                detail: "剪贴板图像（待发送）"));
        }
        // The Wave 7 first-slice: persisted Sources
        // (clipboard snapshots) land below the
        // pending-image list. "clipboard" kind drives
        // the same icon-glyph path; the row carries
        // the captured-at display so the user can see
        // at a glance when the snapshot was taken.
        // Order: newest first — same as the registry's
        // append-on-add order, reversed.
        foreach (var source in persisted.AsEnumerable().Reverse())
        {
            var row = new SourceRowViewModel(
                id: source.Id,
                kind: source.Kind,
                displayName: source.DisplayName,
                detail: FormatSourceDetail(source))
            {
                // 1.0.1: hand the full Source
                // reference to the row so the XAML
                // Click handler in
                // EnvironmentPanelView.axaml.cs can
                // cast Tag → row.Source and raise
                // the panel's
                // InsertReferenceRequested event.
                // MainWindow.xaml.cs subscribes to
                // that event to read the live
                // composer CaretIndex at click time.
                Source = source,
            };
            Sources.Add(row);
        }
    }

    // 1.0.1: the previous "InsertReference" static helper
    // is gone — the click path is now
    //   XAML Click → MainWindow.xaml.cs SourceInsert_OnClick
    //   → AgentHostViewModel.InsertSourceReferenceAtCaret
    // because the helper couldn't see the TextBox's live
    // CaretIndex from the VM layer (command bindings run
    // outside the visual tree). All the dedup / spacing
    // logic moved into InsertSourceReferenceAtCaret.

    private static string FormatSourceDetail(AIChat.Domain.Sources.Source source)
    {
        // The web-fetch path stamps Metadata["url"];
        // surface that in the row detail so the user
        // can see the source at a glance.
        if (source.Metadata.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            return url;
        }
        return source.Kind switch
        {
            "clipboard" => $"剪贴板快照 · {source.CapturedAt.LocalDateTime:MM-dd HH:mm}",
            _ => source.CapturedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
        };
    }

    // Pulled out of RecountSources so the
    // SourceRegistry.Changed subscription can re-mirror
    // when the user adds a new snapshot from anywhere
    // (the +/Add menu, the standalone "剪贴板快照"
    // button, a follow-up "auto-snapshot on ⌘C" path).
    private void RefreshSources() => RecountSources();

    // Re-mirror the supervisor's process snapshot into the panel.
    // The supervisor is the source of truth — every Changed event
    // (start / stop / exit / reload) fires this path. N is small
    // (handful of dev servers), so the Clear + re-add is cheap and
    // avoids the per-row PropertyChanged subscription overhead.
    //
    // Internal so headless tests can drive the sync directly
    // without the Avalonia dispatcher — the production
    // OnSupervisorChanged handler always marshals through
    // Dispatcher.UIThread.Post, which the headless test host
    // doesn't pump. Tests that need to assert on the panel's
    // mirror call this method and skip the marshal. Visibility
    // is wired via [InternalsVisibleTo("AIChat.Tests")] on
    // AIChat.App.Avalonia.csproj (Wave 11 review fix).
    internal void SyncBackgroundProcesses()
    {
        if (_attachedSupervisor is null) return;
        BackgroundProcesses.Clear();
        // Newest-first so the user sees "the most recent thing I
        // started" at the top of the section. The supervisor's own
        // snapshot is insertion order, so we sort by StartedAt
        // desc.
        var ordered = _attachedSupervisor.Processes
            .OrderByDescending(p => p.StartedAt ?? DateTimeOffset.MinValue)
            .ToList();
        foreach (var process in ordered)
        {
            BackgroundProcesses.Add(new BackgroundProcessViewModel(
                process, _attachedSupervisor, _clipboard));
        }
    }

    // Pulls fresh git state. Safe to call repeatedly — the underlying
    // WorkspaceChangeService shells out to `git status` so each call is
    // a real disk read (cheap enough to call on panel open + on user
    // refresh; we don't auto-poll on a timer in Sprint 0.5).
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var project = _sidebar.CurrentProject;
        if (project is null || string.IsNullOrWhiteSpace(project.TryGetPrimaryPath()))
        {
            IsProjectRequired = true;
            BranchName = "(未选择项目)";
            ChangeAdded = 0;
            ChangeRemoved = 0;
            LastRefreshDisplay = DateTime.Now.ToString("HH:mm:ss");
            RefreshDeliverables();
            // Reload the supervisor too — restart-recovery is
            // the supervisor's job, not the panel's, so we
            // delegate and let the Changed event re-mirror the
            // list. Don't block the panel mount on the reload:
            // the synchronous snapshot we already have is
            // good enough for the empty state.
            _ = _processSupervisor.ReloadAsync(ct);
            return;
        }

        IsProjectRequired = false;

        try
        {
            var changeSet = await _workspace.GetChangesAsync(project.TryGetPrimaryPath(), 200, ct);
            BranchName = string.IsNullOrWhiteSpace(changeSet.Branch)
                ? "(无分支信息)"
                : changeSet.Branch.TrimStart('#', ' ').Trim();
            // We don't have a separate +/- split from the current shape of
            // WorkspaceChangeSet; surface the total change count and a
            // placeholder for +/- until Wave 6 adds per-status counters
            // to the workspace service. This is intentionally honest:
            // the user sees "变更 (1 个文件)" not a fabricated +12 -3.
            var total = changeSet.Changes.Count;
            ChangeAdded = total;
            ChangeRemoved = 0;
            LastRefreshDisplay = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            // Don't fabricate "(no changes)" when the real failure
            // is "git isn't installed" or "this isn't a git repo".
            // The BranchName carries the error so the user can
            // tell at a glance.
            BranchName = $"(git 错误: {ex.Message})";
            ChangeAdded = 0;
            ChangeRemoved = 0;
        }
        finally
        {
            RefreshDeliverables();
            // Reload after git so the panel mount sees the latest
            // process state in one pass. The fire-and-forget
            // pattern matches how AttachTo's initial Sync* paths
            // already work; if the reload is slow, the user sees
            // the last snapshot until the new one lands.
            _ = _processSupervisor.ReloadAsync(ct);
        }
    }

    private void RefreshDeliverables()
    {
        Deliverables.Clear();
        try
        {
            var project = _sidebar.CurrentProject;
            if (project is null) return;
            var root = project.TryGetPrimaryPath();
            if (string.IsNullOrWhiteSpace(root)) return;
            var art = Path.Combine(root, "art");
            if (!Directory.Exists(art)) return;
            var files = new DirectoryInfo(art)
                .EnumerateFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(5)
                .ToList();
            foreach (var file in files)
            {
                Deliverables.Add(new DeliverableViewModel(
                    file.Name, file.FullName, file.Length, file.LastWriteTimeUtc));
            }
        }
        catch
        {
            // A permissions error or a vanished directory
            // shouldn't crash the panel — the empty state is
            // the safest fallback.
        }
    }

    // Stops a background process from a row button. Routed through
    // the panel so the XAML can bind Click="..." to a code-behind
    // handler that resolves the VM. The row's own StopCommand
    // already targets the right process id, so this is just a
    // pass-through to keep the XAML's Click syntax consistent
    // with the rest of the panel.
    public async Task StopBackgroundProcessAsync(string? processId)
    {
        if (string.IsNullOrWhiteSpace(processId)) return;
        await _processSupervisor.StopAsync(processId);
    }

    // 1.0.1: drop a persisted Source from the
    // registry. The Sources panel's per-row
    // "×" button routes through this — the
    // registry fires Changed, the panel's
    // subscription re-mirrors, and the row
    // disappears. No-op when the id doesn't
    // match a persisted source (e.g. the row
    // is a pending attachment that shares the
    // Sources list visually but is owned by
    // PendingAttachments, not the registry).
    public async Task RemoveSourceAsync(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        await _sourceRegistry.RemoveAsync(sourceId);
    }
}
