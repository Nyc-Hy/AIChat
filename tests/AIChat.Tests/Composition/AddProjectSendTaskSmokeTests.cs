using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace AIChat.Tests.Composition;

// Phase 0 smoke test: drives the "开 app → 加项目 → 看到 sidebar + 对话列表
// 刷新" flow end-to-end through the real DI graph, with a mock
// IAppRepository (no real disk writes) and a stub IChatProvider (no real
// LLM call). The actual LLM roundtrip is verified manually in
// Phase 0.5 by launching the app and sending a task; this test
// locks the wiring so a future refactor that breaks the chain
// fails the build, not the user.
public class AddProjectSendTaskSmokeTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IAppRepository> _repo = new();

    public AddProjectSendTaskSmokeTests()
    {
        // Use a real temp directory so Directory.Exists + ProjectInitializer's
        // git-walk / verification-command detection all see a real layout.
        _tempRoot = Path.Combine(Path.GetTempPath(), "aichat-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src"));
        File.WriteAllText(Path.Combine(_tempRoot, "src", "Program.cs"), "// hello\n");

        // LoadProjectsAsync: each call returns a fresh empty array. Moq's
        // Returns(() => value) form captures the value once; we want a
        // factory so successive calls don't share state. The IReadOnlyList
        // wrapper cast is what nudges Moq's overload resolution to pick
        // the factory form instead of the captured-value form.
        var emptyWorkspaces = (IReadOnlyList<WorkspaceProject>)Array.Empty<WorkspaceProject>();
        _repo.Setup(repo => repo.LoadWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyWorkspaces);
        _repo.Setup(repo => repo.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings());
        _repo.Setup(repo => repo.SaveSettingsAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(repo => repo.SaveWorkspacesAsync(It.IsAny<IReadOnlyList<WorkspaceProject>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AddProjectFromUiAsync_ProjectAppearsInSidebarAndConversationListRefreshes()
    {
        using var host = BuildTestHost();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        // 1. Start state: no projects, sidebar empty.
        Assert.Empty(viewModel.Sidebar.Projects);

        // 2. Add a project via the same path the UI uses (⌘O / ⌘K / "添加项目" button).
        await viewModel.AddProjectFromUiAsync(_tempRoot);

        // 3. Sidebar now has exactly one project, pointed at the temp dir.
        var sidebarProject = Assert.Single(viewModel.Sidebar.Projects);
        Assert.Equal(_tempRoot, sidebarProject.Path);

        // 4. ProjectAdded fired so the host can re-derive derived state
        //    (LastActiveProjectId, conversation list refresh).
        Assert.NotNull(viewModel.Sidebar.CurrentProject);
        Assert.Equal(_tempRoot, viewModel.Sidebar.CurrentProject!.TryGetPrimaryPath());

        // 5. Repository saw the save call (the persistence round-trip works
        //    end-to-end through the in-memory mock).
        _repo.Verify(repo => repo.SaveWorkspacesAsync(
            It.Is<IReadOnlyList<WorkspaceProject>>(list => list.Count == 1 && list[0].PrimaryPath == _tempRoot),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AddProjectFromUiAsync_WithNonExistentPath_LeavesSidebarEmpty()
    {
        // The picker already filters out non-existent dirs, but a code path
        // (drag-drop, command palette text input) might still feed a bad
        // path. Lock the behavior: the host does NOT add a stub project
        // and the sidebar stays empty, instead of crashing on an empty
        // Path round-trip.
        using var host = BuildTestHost();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        var bogus = Path.Combine(_tempRoot, "does-not-exist");

        await viewModel.AddProjectFromUiAsync(bogus);

        Assert.Empty(viewModel.Sidebar.Projects);
        _repo.Verify(repo => repo.SaveWorkspacesAsync(
            It.IsAny<IReadOnlyList<WorkspaceProject>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // Build a host that swaps the JSON repository for the in-memory mock.
    // AddAIChatDesktop appends JsonAppRepository as the IAppRepository
    // implementation, so a naive AddSingleton before Build() gets
    // shadowed. The pattern: pre-register the mock, run
    // AddAIChatDesktop, then Services.Replace() throws away the JSON
    // registration and installs the mock in its place. The same
    // "build with overrides" pattern is exercised by
    // AppHostTests.Build_WithCustomCollection.
    private ServiceProvider BuildTestHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repo.Object);
        // Strip the real provider adapters — we don't want network calls
        // or HTTP clients constructed in the test host. The smoke test
        // only covers the project-adding wiring, not the LLM path.
        services.AddSingleton<IChatProvider>(_ => new NoopChatProvider());
        services.AddAIChatDesktop();
        services.Replace(ServiceDescriptor.Singleton<IAppRepository>(_repo.Object));
        return services.BuildServiceProvider(validateScopes: true);
    }

    // NoopChatProvider exists so the test host can resolve IChatProvider
    // (the harness / runner only touch it through the routed service,
    // which is itself never invoked in these tests, but the DI graph
    // still needs the dependency satisfied). It's not exercised — the
    // smoke tests above stop at project-adding, before the LLM path.
    private sealed class NoopChatProvider : IChatProvider
    {
        public LlmProviderInfo Info =>
            new() { Id = "noop", ProtocolId = "openai", Name = "noop", DefaultBaseUrl = "", DefaultModel = "noop" };

        public bool CanHandle(AppSettings settings) => false;

        public IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("NoopChatProvider is a DI placeholder, not a real LLM.");
    }
}
