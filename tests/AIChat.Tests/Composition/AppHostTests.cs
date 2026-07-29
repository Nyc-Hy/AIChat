using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Tools;
using AIChat.Providers.Anthropic;
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
    public void Build_RegistersBothProviderAdapters()
    {
        using var host = AppHost.Build();

        var providers = host.GetServices<IChatProvider>().ToList();

        Assert.Equal(2, providers.Count);
        Assert.Contains(providers, item => item is OpenAICompatibleChatProvider);
        Assert.Contains(providers, item => item is AnthropicChatProvider);
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
}
