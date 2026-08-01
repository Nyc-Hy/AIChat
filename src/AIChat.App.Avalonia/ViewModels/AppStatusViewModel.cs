using System.ComponentModel;
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
public sealed partial class AppStatusViewModel : ObservableObject, IDisposable
{
    private readonly ProjectSidebarViewModel _sidebar;

    public AppStatusViewModel(ProjectSidebarViewModel sidebar)
    {
        _sidebar = sidebar;
        _sidebar.PropertyChanged += OnSidebarPropertyChanged;
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
    // doesn't read "AIChat / 未配置路径"). IsReady /
    // NeedsConfiguration drive the compact status pills.
    public bool HasProject => !string.IsNullOrWhiteSpace(_sidebar.SelectedProjectName)
                              && _sidebar.SelectedProjectName != "未配置路径";

    public bool IsReady => Readiness == "可运行";
    public bool NeedsConfiguration => Readiness == "需要密钥" || Readiness == "需检查";

    public string Greeting => HasProject ? "今天要完成什么？" : "选一个项目开始";
    public string SubGreeting => HasProject
        ? "输入目标后，AIChat 会读取项目上下文并在风险操作前询问你。"
        : "添加本地代码仓库，让 AIChat 读取上下文后再开始任务。";

    public string StatusBarModel => string.IsNullOrEmpty(ActiveModel)
        ? ActiveProvider
        : $"{ActiveProvider} · {ActiveModel}";

    // Forward PropertyChanged for the sidebar's
    // SelectedProjectName to the derived properties. This is
    // the same forwarding the inline version did in
    // MainWindowViewModel (the comment on the old code said
    // "SelectedProjectName drives HasProject, Greeting and
    // SubGreeting; re-raise here so the bindings pick up the
    // change") — moved here so MainWindowViewModel doesn't
    // need to know which derived properties depend on which
    // upstream.
    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSidebarViewModel.SelectedProjectName))
        {
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(SubGreeting));
        }
    }

    partial void OnActiveProviderChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnActiveModelChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnReadinessChanged(string value)
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(NeedsConfiguration));
    }

    public void Dispose()
    {
        _sidebar.PropertyChanged -= OnSidebarPropertyChanged;
    }
}
