using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.App.Avalonia.Views;
using AIChat.Application.Artifacts;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Composition;

// Smoke tests for PR-1: the composition root builds a working service
// container and exposes the services that the desktop app relies on. The full
// view-model and view resolution tests live behind a headless Avalonia fixture
// (PR-10) — here we only assert the container graph is wired correctly.
public class AppHostTests
{
    [Fact]
    public void Build_ReturnsServiceProvider()
    {
        using var host = AppHost.Build();

        Assert.NotNull(host);
    }

    [Fact]
    public void Build_RegistersPersistenceAgainstAbstraction()
    {
        using var host = AppHost.Build();

        var repository = host.GetService<IAppRepository>();

        Assert.NotNull(repository);
        Assert.IsType<JsonAppRepository>(repository);
    }

    [Fact]
    public void Build_RegistersOpenAICompatibleAdapter()
    {
        // 2026-08-02: AIChat ships with MiniMax only, so the routed
        // chat completion service gets a single OpenAI-compatible
        // adapter (MiniMax is OpenAI-protocol). The previous
        // "RegistersBothProviderAdapters" test (OpenAI + Anthropic)
        // was retired when the Anthropic provider was removed.
        using var host = AppHost.Build();

        var providers = host.GetServices<IChatProvider>().ToList();

        Assert.Single(providers);
        Assert.Contains(providers, item => item is OpenAICompatibleChatProvider);
    }

    [Fact]
    public void Build_ResolvesRoutedChatCompletionService()
    {
        using var host = AppHost.Build();

        var chatService = host.GetService<IChatCompletionService>();

        Assert.NotNull(chatService);
        Assert.IsType<RoutedChatCompletionService>(chatService);
    }

    [Fact]
    public void Build_ResolvesDefaultToolRegistry()
    {
        using var host = AppHost.Build();

        var registry = host.GetService<AgentToolRegistry>();

        Assert.NotNull(registry);
        Assert.NotEmpty(registry.All);
    }

    [Fact]
    public void Build_ResolvesSingletonsAsTheSameInstance()
    {
        // IAppRepository is a pure (non-UI) singleton, so it's safe to verify
        // singleton semantics without dragging in the Avalonia platform. The
        // MainWindow itself is registered as a singleton too but resolving it
        // requires the Avalonia windowing platform, which is set up by the
        // real app entry point — that's covered by the launch-time smoke
        // check, not by these unit tests.
        using var host = AppHost.Build();

        var first = host.GetRequiredService<IAppRepository>();
        var second = host.GetRequiredService<IAppRepository>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Build_WithCustomCollection_PreservesCallerRegistrations()
    {
        // Later PRs (test fixtures, scenario tests) will pre-register fakes in
        // the collection and rely on AddAIChatDesktop leaving those entries
        // alone. Verify the contract here so the test pattern is locked in.
        var services = new ServiceCollection();
        var sentinel = new object();
        services.AddSingleton(sentinel);
        using var host = AppHost.Build(services);

        Assert.Same(sentinel, host.GetService<object>());
    }

    // Locks the DI container graph so a future refactor that
    // forgets to register a new service (or accidentally drops
    // one) breaks the build / tests. The case this exists to
    // catch: PR-1 to PR-9 each landed new services (IWorkspaceChangeService
    // most recently) and the host's GetRequiredService<MainWindow>()
    // would throw on startup if any of them were missing. The
    // smoke test that caught that lived only in AppHostTests; this
    // list is the explicit lock so the pattern can't drift.
    [Theory]
    [InlineData(typeof(IAppRepository))]
    [InlineData(typeof(IWorkspaceChangeService))]
    [InlineData(typeof(AgentToolRegistry))]
    [InlineData(typeof(IChatProvider), true)]
    [InlineData(typeof(IChatCompletionService))]
    [InlineData(typeof(ProviderConnectionTester))]
    [InlineData(typeof(ISettingsHolder))]
    [InlineData(typeof(ProviderConfigViewModel))]
    [InlineData(typeof(SettingsViewModel))]
    [InlineData(typeof(ProjectSidebarViewModel))]
    [InlineData(typeof(ConversationListViewModel))]
    [InlineData(typeof(ToolApprovalViewModel))]
    [InlineData(typeof(MemoryEditorViewModel))]
    [InlineData(typeof(GitStatusViewModel))]
    [InlineData(typeof(PluginsViewModel))]
    [InlineData(typeof(AIChat.Application.Plugins.IPluginRegistry))]
    [InlineData(typeof(ScheduledViewModel))]
    [InlineData(typeof(AIChat.Application.Scheduled.IScheduledTaskRegistry))]
    [InlineData(typeof(SitesViewModel))]
    [InlineData(typeof(AIChat.Application.Sites.ISiteRegistry))]
    [InlineData(typeof(InputArtifactFileStore))]
    [InlineData(typeof(IApprovalService))]
    [InlineData(typeof(IThemeService))]
    [InlineData(typeof(IToastService))]
    [InlineData(typeof(AvaloniaProjectPicker))]
    [InlineData(typeof(IProjectPicker))]
    [InlineData(typeof(AvaloniaClipboardService))]
    [InlineData(typeof(IClipboardService))]
    [InlineData(typeof(MainWindowViewModel))]
    [InlineData(typeof(AIChat.Application.BackgroundProcesses.IBackgroundProcessSupervisor))]
    public void Build_ResolvesTopLevelService(Type serviceType, bool expectMultiple = false)
    {
        using var host = AppHost.Build();

        if (expectMultiple)
        {
            var instances = host.GetServices(serviceType);
            Assert.NotEmpty(instances);
        }
        else
        {
            var instance = host.GetService(serviceType);
            Assert.NotNull(instance);
        }
    }
}
