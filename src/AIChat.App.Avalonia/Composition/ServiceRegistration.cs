using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.ViewModels;
using AIChat.App.Avalonia.Views;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Tools;
using AIChat.Providers.Anthropic;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.App.Avalonia.Composition;

// Centralised DI registration for the Avalonia desktop host.
internal static class ServiceRegistration
{
    public static IServiceCollection AddAIChatDesktop(this IServiceCollection services)
    {
        // Local JSON persistence. Registered against the abstraction so tests
        // can swap in an in-memory implementation later.
        services.AddSingleton<IAppRepository, JsonAppRepository>();

        // Tool registry. CreateDefault() is the same call the ViewModel used
        // before; keep behaviour identical for this PR.
        services.AddSingleton(_ => AgentToolRegistry.CreateDefault());

        // Provider adapters. RoutedChatCompletionService resolves these via
        // IEnumerable<IChatProvider>, so each provider is registered on its
        // own and the framework composes the enumerable.
        services.AddSingleton<IChatProvider, OpenAICompatibleChatProvider>();
        services.AddSingleton<IChatProvider, AnthropicChatProvider>();
        services.AddSingleton<IChatCompletionService, RoutedChatCompletionService>();
        services.AddSingleton<ProviderConnectionTester>();

        // PR-2: shared settings holder. Both MainWindowViewModel and
        // ProviderConfigViewModel need to read and mutate the same AppSettings
        // instance. The holder is the single source of truth and breaks the
        // constructor cycle between the two view-models.
        services.AddSingleton<ISettingsHolder, SettingsHolder>();

        // Main window + its view-model. Singleton because Avalonia hosts a
        // single MainWindow and the ViewModel owns cross-cutting state
        // (selected project, conversation, pending approval, run metrics).
        services.AddSingleton<ProviderConfigViewModel>();
        services.AddSingleton<ProjectSidebarViewModel>();
        services.AddSingleton<ConversationListViewModel>();
        services.AddSingleton<SessionInsightsViewModel>();
        services.AddSingleton<ToolApprovalViewModel>();
        services.AddSingleton<IApprovalService, UIBoundApprovalService>();
        services.AddSingleton<IThemeService, FluentThemeService>();
        services.AddSingleton<IToastService, ToastService>();
        // ProjectPicker needs a TopLevel which is only available after the
        // window is constructed. The view code-behind sets TopLevel on
        // the concrete AvaloniaProjectPicker once the window is ready.
        services.AddSingleton<AvaloniaProjectPicker>();
        services.AddSingleton<IProjectPicker>(sp => sp.GetRequiredService<AvaloniaProjectPicker>());
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
