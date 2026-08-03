using System.Collections.Specialized;
using System.ComponentModel;
using AIChat.Abstractions.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// App-wide status / readiness surface — split out from
// MainWindowViewModel so the host's only remaining "display
// state" responsibility is the modals-open flag, palette
// registration, and the Refresh entry point. This VM owns the
// 4 backing fields the status bar / empty-state chrome /
// header pills all bind to (ActiveProvider / ActiveModel /
// Readiness / IsProviderTesting) plus the 6 derived properties
// computed off them and the sidebar selection (HasProject /
// IsReady / NeedsConfiguration / Greeting / SubGreeting /
// StatusBarModel). It subscribes to the sidebar's PropertyChanged
// so derived properties re-raise automatically when the project
// flips — MainWindowViewModel no longer needs to forward those
// manually.
//
// Pattern matches MessageScrollState (extracted earlier): thin
// VM, single purpose, no orchestration. MainWindowViewModel
// assigns to these fields via `_appStatus.ActiveProvider = ...`
// and the XAML binds via `AppStatus.ActiveProvider` etc.
//
// Lifetime: singleton in DI alongside the host. The Sidebar
// subscription lives for the process lifetime — same shape as
// the host's own event subscriptions, no explicit Unwire
// needed because nothing in this app ever disposes the host.
public sealed partial class AppStatusViewModel : ObservableObject
{
    private readonly ProjectSidebarViewModel _sidebar;

    public AppStatusViewModel(ProjectSidebarViewModel sidebar)
    {
        _sidebar = sidebar;
        _sidebar.PropertyChanged += OnSidebarPropertyChanged;
        // Projects is an ObservableCollection — its Clear/Add fire
        // CollectionChanged, not PropertyChanged. The handler below
        // re-raises the derived properties whenever the project list
        // changes so the empty-state hero / CTAs flip to the 4
        // quick-action cards the moment a project is loaded.
        _sidebar.Projects.CollectionChanged += OnSidebarProjectsChanged;
    }

    [ObservableProperty]
    private string activeProvider = "正在加载...";

    [ObservableProperty]
    private string activeModel = "";

    [ObservableProperty]
    private string readiness = "检查中";

    [ObservableProperty]
    private bool isProviderTesting;

    // 1.0 Beta: derive the top status, breadcrumb visibility and
    // status-bar text from the same handful of fields so the
    // XAML can stay declarative. HasProject hides the project
    // crumb when no project is selected (so the breadcrumb
    // doesn't read "AIChat / 未配置路径") and drives the empty
    // state first-run CTA vs. 4 quick-action card switch. IsReady /
    // NeedsConfiguration drive the compact status pills.
    //
    // HasProject is true only when the sidebar actually holds a
    // real project. Sidebar.SelectedProjectName defaults to
    // "未选择项目" as a display hint for the breadcrumb, but that
    // string is not a "real" project — it means "no project loaded
    // yet". Checking Projects.Count > 0 also defends against a
    // user who has a project card but a missing Path (which makes
    // Sidebar.Refresh skip it; see ProjectSidebarViewModel.Refresh).
    public bool HasProject => _sidebar.Projects.Count > 0
                              && !string.IsNullOrWhiteSpace(_sidebar.SelectedProjectName)
                              && _sidebar.SelectedProjectName != "未配置路径"
                              && _sidebar.SelectedProjectName != "未选择项目";

    public bool IsReady => HasProject && Readiness == "可运行";
    public bool NeedsConfiguration => Readiness == "需要密钥" || Readiness == "需检查";

    public string Greeting => HasProject ? "今天要完成什么？" : "选一个项目开始";
    public string SubGreeting => HasProject
        ? "输入目标后，AIChat 会读取项目上下文并在风险操作前询问你。"
        : "添加本地代码仓库，让 AIChat 读取上下文后再开始任务。";

    public string StatusBarModel => string.IsNullOrEmpty(ActiveModel)
        ? ActiveProvider
        : $"{ActiveProvider} · {ActiveModel}";

    // Sprint 0.5 polish: surfaced to the status-bar's shield icon so the
    // user gets a visual cue (not just the "（隔离会话…）" text suffix)
    // when the session is reading / writing to a temp data root instead
    // of the real ~/Library/Application Support/AIChat. AppRuntimeProfile
    // is a static helper that resolves AICHAT_ISOLATED_DATA_ROOT once at
    // startup.
    public bool IsIsolatedMode => AppRuntimeProfile.IsIsolated;

    // Forward PropertyChanged for the sidebar's
    // SelectedProjectName / Projects to the derived properties.
    // This is the same forwarding the inline version did in
    // MainWindowViewModel (the comment on the old code said
    // "SelectedProjectName drives HasProject, Greeting and
    // SubGreeting; re-raise here so the bindings pick up the
    // change") — moved here so MainWindowViewModel doesn't need
    // to know which derived properties depend on which upstream.
    // Projects is included because HasProject also gates on
    // Projects.Count > 0 (the sidebar's "未选择项目" hint defaults
    // to a non-empty SelectedProjectName, but with an empty
    // Projects list we still want the empty-state hero / CTAs
    // to show, not the 4 quick-action cards).
    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSidebarViewModel.SelectedProjectName)
            || e.PropertyName == nameof(ProjectSidebarViewModel.Projects))
        {
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(SubGreeting));
        }
    }

    // ObservableCollection changes don't fire PropertyChanged on
    // the owning VM — only CollectionChanged on the collection
    // itself. We re-raise the derived properties here so the XAML
    // picks up "project list went empty / got first item" without
    // the host having to forward manually.
    private void OnSidebarProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(SubGreeting));
    }

    partial void OnActiveProviderChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnActiveModelChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnReadinessChanged(string value)
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(NeedsConfiguration));
    }
}
