using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.ViewModels;
using AIChat.App.Avalonia.Views;
using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Plugins;
using AIChat.Application.Scheduled;
using AIChat.Application.Sites;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Application.Artifacts;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIChat.App.Avalonia.Composition;

// Centralised DI registration for the Avalonia desktop host.
public static class ServiceRegistration
{
    public static IServiceCollection AddAIChatDesktop(this IServiceCollection services)
    {
        // Local JSON persistence. Registered against the abstraction so tests
        // can swap in an in-memory implementation later.
        services.TryAddSingleton<IAppRepository, JsonAppRepository>();

        // Git-backed workspace change / diff service. MainWindowViewModel
        // (for the /git bubble) and GitStatusViewModel (for the modal
        // file list + diff) both consume IWorkspaceChangeService — if
        // it isn't registered here, GetRequiredService<MainWindow>()
        // blows up at startup. Singleton because the service is a
        // pure facade over Process.Start; no per-request state.
        services.AddSingleton<IWorkspaceChangeService, WorkspaceChangeService>();
        services.AddSingleton<InputArtifactFileStore>();

        // Tool registry. CreateDefault() is the same call the ViewModel used
        // before; keep behaviour identical for this PR.
        services.AddSingleton(_ => AgentToolRegistry.CreateDefault());

        // Wave 8 (parity plan §7 Wave 8): local plugin registry.
        // Scans AppRuntimeProfile.PluginsDirectory for plugin.json
        // files, validates them, and presents the enabled set to
        // the host (PluginsView + AgentToolRegistry wiring). The
        // registry is constructed eagerly so the first ReloadAsync
        // runs in the background before any view binds to it —
        // the Plugins tab shows "(加载中…)" until the first Changed
        // event fires.
        services.AddSingleton<IPluginRegistry, PluginRegistry>();

        // Wave 9 (parity plan §7 Wave 9): Scheduled + Sites
        // registries. Same eager-construction pattern as the
        // plugin registry — the VMs reload in the background
        // so the modal can show real data the moment the
        // user opens it.
        services.AddSingleton<IScheduledTaskRegistry, ScheduledTaskRegistry>();
        services.AddSingleton<ISiteRegistry, SiteRegistry>();

        // Wave 7 follow-up (plan §13 P0 risk "整个子进程树"):
        // BackgroundProcessSupervisor. Owns the persistence +
        // process-tree kill path; the Environment panel
        // mirrors its state, and the Sites preview routes
        // its real local-server start through this service.
        services.AddSingleton<IBackgroundProcessSupervisor, BackgroundProcessSupervisor>();

        // Provider adapter. 2026-08-02: AIChat ships with MiniMax
        // only (M3 is the current flagship), so the routed service
        // gets a single OpenAI-compatible adapter. The adapter's
        // CanHandle() matches every provider with protocolId=openai,
        // which is exactly the MiniMax shape.
        services.AddSingleton<IChatProvider, OpenAICompatibleChatProvider>();
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
        // Wave 8 (parity plan §7 Wave 8): PluginsViewModel is
        // constructed eagerly so the IPluginRegistry's first
        // ReloadAsync fires in the background before the user
        // opens the modal. (The VM is also reused — the modal's
        // Bindings see the same instance the host holds, so any
        // enable / disable call from a follow-up slice lands in
        // the same state.)
        services.AddSingleton<PluginsViewModel>();
        // Wave 9 (parity plan §7 Wave 9): Scheduled + Sites VMs.
        // Same eager-construction pattern as Plugins — both
        // registries reload in the background before the user
        // opens the modals.
        services.AddSingleton<ScheduledViewModel>();
        services.AddSingleton<SitesViewModel>();
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
