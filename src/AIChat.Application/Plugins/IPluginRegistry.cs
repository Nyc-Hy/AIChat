namespace AIChat.Application.Plugins;

// Wave 8 (parity plan §7 Wave 8): the surface area the host
// (DI / PluginsView / AgentToolRegistry wiring) needs from the
// plugin system. Lives next to the implementation so the
// desktop host takes a direct dependency on
// AIChat.Application.Plugins (no separate abstraction assembly
// — the only project that consumes plugins today is the
// Avalonia host, and the existing application-level services
// like IWorkspaceChangeService / IProjectFileIndexBuilder /
// ProviderConnectionTester follow the same pattern).
//
// Scope of this first slice:
//   * discover installed plugins from a configured directory
//   * enable / disable a plugin (persisted across restarts)
//   * reload after a manual file drop (user copies a plugin
//     folder, clicks "刷新")
// Install / uninstall / search / OAuth-capable connectors land
// in a follow-up slice — the design doc requires an install
// trust chain and capability grant model that's its own PR.
public interface IPluginRegistry
{
    // Stable directory the registry scans. Resolved from
    // AppRuntimeProfile.PluginsDirectory at construction; tests
    // can pass a different value via the concrete ctor.
    string PluginsDirectory { get; }

    // The current set of enabled plugins, in the order they
    // were loaded. Updated by ReloadAsync / SetEnabledAsync;
    // the host binds a list view to this.
    IReadOnlyList<PluginManifest> Plugins { get; }

    // Loader diagnostics (broken manifest files, validation
    // errors, etc.). Surfaced in the Plugins view so the user
    // can see "this plugin didn't load because…" without
    // digging through logs.
    IReadOnlyList<PluginDiagnostic> Diagnostics { get; }

    // Fires after ReloadAsync / SetEnabledAsync mutate Plugins
    // or Diagnostics. Hosts subscribe once and re-bind their
    // list view.
    event EventHandler? Changed;

    // Re-scan the plugins directory. The previous in-memory
    // list is discarded; persistence is per-plugin (the
    // registry's own state sidecar maps plugin id → enabled
    // bool) so a reload preserves enable/disable choices.
    Task ReloadAsync(CancellationToken cancellationToken = default);

    // Flip a plugin on or off. Returns true if the state
    // changed; false if the plugin id was unknown or the new
    // state equals the current state. Persists the choice to
    // the state sidecar.
    Task<bool> SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default);
}
