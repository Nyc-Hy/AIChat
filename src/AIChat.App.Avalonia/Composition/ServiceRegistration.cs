using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.ViewModels;
using AIChat.App.Avalonia.Views;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Providers.Anthropic;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.App.Avalonia.Composition;

// Centralised DI registration for the Avalonia desktop host.
public static class ServiceRegistration
{
    public static IServiceCollection AddAIChatDesktop(this IServiceCollection services)
    {
        // Local JSON persistence. Registered against the abstraction so tests
        // can swap in an in-memory implementation later.
        services.AddSingleton<IAppRepository, JsonAppRepository>();

        // Git-backed workspace change / diff service. MainWindowViewModel
        // (for the /git bubble) and GitStatusViewModel (for the modal
        // file list + diff) both consume IWorkspaceChangeService — if
        // it isn't registered here, GetRequiredService<MainWindow>()
        // blows up at startup. Singleton because the service is a
        // pure facade over Process.Start; no per-request state.
        services.AddSingleton<IWorkspaceChangeService, WorkspaceChangeService>();

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
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ProjectSidebarViewModel>();
        services.AddSingleton<ConversationListViewModel>();
        services.AddSingleton<ToolApprovalViewModel>();
        services.AddSingleton<MemoryEditorViewModel>();
        services.AddSingleton<GitStatusViewModel>();
        services.AddSingleton<IApprovalService, UIBoundApprovalService>();
        services.AddSingleton<IThemeService, FluentThemeService>();
        services.AddSingleton<IToastService, ToastService>();
        // ProjectPicker + ClipboardService both need a TopLevel which is
        // only available after the window is constructed. The view
        // code-behind sets TopLevel on the concrete implementation once
        // the window is ready.
        services.AddSingleton<AvaloniaProjectPicker>();
        services.AddSingleton<IProjectPicker>(sp => sp.GetRequiredService<AvaloniaProjectPicker>());
        services.AddSingleton<AvaloniaClipboardService>();
        services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<AvaloniaClipboardService>());
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
