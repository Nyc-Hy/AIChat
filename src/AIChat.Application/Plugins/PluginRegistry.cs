using System.Text.Json;
using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Plugins;

// Concrete IPluginRegistry. Scans `pluginsDirectory` for
// `plugin.json` manifests, validates each one, and presents
// the enabled subset as `Plugins`.
//
// Persistence: the manifest itself only carries the *author*
// default (`enabled: true|false` in the JSON). The user's
// runtime choice (toggle on / off in the UI) lives in a
// sidecar `<pluginsDirectory>/.state.json` so it survives app
// restarts and is editable by power users. The state file is
// keyed by plugin id; a missing key means "fall back to the
// manifest's enabled flag".
//
// Threading: the registry is a singleton; all mutations go
// through ReloadAsync / SetEnabledAsync which take a lock
// briefly and then fire `Changed` on the captured instance.
// Read access (Plugins, Diagnostics) is unlocked; the worst
// that can happen is the host reads a half-updated list,
// which Avalonia re-binds on the next Changed event.
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly string _stateFilePath;
    private readonly object _gate = new();

    public PluginRegistry(string? pluginsDirectory = null)
    {
        PluginsDirectory = pluginsDirectory ?? AppRuntimeProfile.PluginsDirectory;
        _stateFilePath = Path.Combine(PluginsDirectory, ".state.json");
    }

    public string PluginsDirectory { get; }

    private List<PluginManifest> _plugins = [];
    private List<PluginDiagnostic> _diagnostics = [];
    private Dictionary<string, bool> _state = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PluginManifest> Plugins
    {
        get { lock (_gate) { return _plugins.ToArray(); } }
    }

    public IReadOnlyList<PluginDiagnostic> Diagnostics
    {
        get { lock (_gate) { return _diagnostics.ToArray(); } }
    }

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loadResult = await PluginManifestLoader
            .LoadDirectoryWithDiagnosticsAsync(PluginsDirectory, cancellationToken)
            .ConfigureAwait(false);

        // Load the user's persisted enable/disable choices before
        // we apply them — missing sidecar → fall back to the
        // manifest's author default.
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);

        var applied = new List<PluginManifest>(loadResult.Manifests.Count);
        foreach (var manifest in loadResult.Manifests)
        {
            // The manifest loader already filtered by `Enabled` for
            // the initial pass; we re-apply the user's choice
            // here so a user can re-enable a plugin the author
            // shipped as `enabled: false` (or vice versa).
            if (state.TryGetValue(manifest.Id, out var isEnabled))
            {
                manifest.Enabled = isEnabled;
            }
            if (manifest.Enabled)
            {
                applied.Add(manifest);
            }
        }

        lock (_gate)
        {
            _plugins = applied;
            _diagnostics = loadResult.Diagnostics.ToList();
            _state = state;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        // Always overwrite the sidecar. "No-op if already in
        // target state" is an optimization we skip here — the
        // sidecar write is cheap, ReloadAsync re-reads the
        // manifest, and the next toggle is the only consumer of
        // the return value. Keep the contract simple: return
        // true if we wrote the sidecar.
        lock (_gate)
        {
            _state[pluginId] = enabled;
        }

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ---- sidecar I/O ----------------------------------------------------

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private async Task<Dictionary<string, bool>> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer
                .DeserializeAsync<Dictionary<string, bool>>(stream, StateJsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return state is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(state, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupted sidecar shouldn't break the app — fall
            // back to manifest defaults and let the user re-toggle
            // to overwrite. The .state.json file stays on disk for
            // post-mortem inspection.
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, bool> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, bool>(_state, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            await using var stream = File.Create(_stateFilePath);
            await JsonSerializer
                .SerializeAsync(stream, snapshot, StateJsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Persistence failure is non-fatal — the next toggle
            // will retry. The in-memory state is still correct for
            // the current session.
        }
    }
}
