using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Projects;
using Moq;

namespace AIChat.Tests.Avalonia;

// Locks the AppStatusViewModel semantics that drive the empty-state
// hero / CTA split:
//   HasProject = sidebar has a real project loaded (not just the
//                "未选择项目" display hint).
//   Greeting / SubGreeting switch off HasProject so the XAML flips
//   between the first-run CTAs and the 4 quick-action cards.
//
// Re-raise on sidebar property change + ObservableCollection change
// is also pinned: pre-fix the empty-state showed the wrong copy on
// a fresh install because (a) the default "未选择项目" hint was
// mistaken for a real project, and (b) the Projects collection's
// first Add() didn't re-raise HasProject.
public class AppStatusViewModelTests : IDisposable
{
    private readonly string _tempRoot;

    public AppStatusViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AIChatAppStatusTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void HasProject_OnFreshSidebarWithNoProjects_IsFalse()
    {
        // Default state: Projects.Count == 0, SelectedProjectName
        // defaults to "未选择项目" (the display hint). HasProject must
        // NOT mistake the hint for a real project, otherwise the
        // empty-state hero / CTA switch renders the wrong copy on
        // first launch.
        var (status, _) = Create();

        Assert.False(status.HasProject);
    }

    [Fact]
    public void HasProject_AfterAddingFirstProject_IsTrue()
    {
        var (status, sidebar) = Create();

        sidebar.Refresh([new ProjectWorkspace
        {
            Id = "a", Name = "Alpha", Path = Path.Combine(_tempRoot, "alpha")
        }]);

        Assert.True(status.HasProject);
    }

    [Fact]
    public void Greeting_TracksHasProject_FlippingBothWays()
    {
        var (status, sidebar) = Create();

        Assert.Equal("选一个项目开始", status.Greeting);
        Assert.Contains("添加本地代码仓库", status.SubGreeting);

        sidebar.Refresh([new ProjectWorkspace
        {
            Id = "a", Name = "Alpha", Path = Path.Combine(_tempRoot, "alpha")
        }]);

        Assert.Equal("今天要完成什么？", status.Greeting);
        Assert.Contains("读取项目上下文", status.SubGreeting);
    }

    [Fact]
    public void HasProject_NotifiesOnSidebarProjectsChange()
    {
        // ObservableCollection.Clear/Add don't fire PropertyChanged on
        // the owning VM — only CollectionChanged on the collection.
        // AppStatusViewModel subscribes to Projects.CollectionChanged
        // and re-raises HasProject so the XAML picks up the
        // "first project loaded" transition. Pre-fix the empty-state
        // hero / CTAs stayed on screen even after a project loaded.
        var (status, sidebar) = Create();
        var notifications = new List<string>();
        status.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? "");

        sidebar.Refresh([new ProjectWorkspace
        {
            Id = "a", Name = "Alpha", Path = Path.Combine(_tempRoot, "alpha")
        }]);

        // Should have re-raised HasProject + Greeting + SubGreeting.
        // The first 3 notifications are from OnSidebarProjectsChanged
        // (collection Add), the next 3 are from OnSidebarPropertyChanged
        // (SelectedProjectName changed). Order isn't strictly pinned
        // (multiple handlers fire) but the count must be >= 3 each.
        Assert.Contains(nameof(AppStatusViewModel.HasProject), notifications);
        Assert.Contains(nameof(AppStatusViewModel.Greeting), notifications);
        Assert.Contains(nameof(AppStatusViewModel.SubGreeting), notifications);
    }

    [Fact]
    public void StatusBarModel_TracksActiveProviderAndModel()
    {
        var (status, _) = Create();

        // ActiveProvider defaults to "正在加载..." and ActiveModel to
        // empty; StatusBarModel falls back to ActiveProvider alone.
        Assert.Equal("正在加载...", status.StatusBarModel);

        status.ActiveProvider = "MiniMax";
        status.ActiveModel = "MiniMax-M2.1";

        Assert.Equal("MiniMax · MiniMax-M2.1", status.StatusBarModel);
    }

    private (AppStatusViewModel status, ProjectSidebarViewModel sidebar) Create()
    {
        var repository = Mock.Of<IAppRepository>();
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, holder);
        var status = new AppStatusViewModel(sidebar);
        return (status, sidebar);
    }
}
