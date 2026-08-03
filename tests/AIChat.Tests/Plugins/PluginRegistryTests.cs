using AIChat.Application.Plugins;

namespace AIChat.Tests.Plugins;

// Wave 8 (parity plan §7 Wave 8): pin the registry contract that
// the PluginsView + AgentToolRegistry wiring depend on. The
// tests run against an isolated temp directory per test so the
// real AppRuntimeProfile.PluginsDirectory isn't touched.
public sealed class PluginRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly PluginRegistry _registry;

    public PluginRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aichat-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _registry = new PluginRegistry(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ReloadAsync_EmptyDirectory_ProducesEmptyPlugins()
    {
        await _registry.ReloadAsync();

        Assert.Empty(_registry.Plugins);
        Assert.Empty(_registry.Diagnostics);
    }

    [Fact]
    public async Task ReloadAsync_PicksUpEnabledPlugin()
    {
        await WritePluginAsync("echo", """
        {
          "id": "echo_plugin",
          "name": "Echo Plugin",
          "version": "0.1.0",
          "enabled": true,
          "tools": [
            { "id": "echo", "description": "Echo", "risk": "read_only",
              "command": { "executable": "echo", "arguments": ["hi"] } }
          ]
        }
        """);

        await _registry.ReloadAsync();

        var plugin = Assert.Single(_registry.Plugins);
        Assert.Equal("echo_plugin", plugin.Id);
        Assert.Equal("Echo Plugin", plugin.Name);
        Assert.Single(plugin.Tools);
    }

    [Fact]
    public async Task ReloadAsync_SkipsDisabledPlugin_WhenNoStateOverride()
    {
        // The author shipped the manifest as `enabled: false`. With
        // no user sidecar the registry should drop it from the
        // enabled set so the host doesn't surface a plugin the
        // author disabled.
        await WritePluginAsync("echo", """
        {
          "id": "echo_plugin",
          "enabled": false,
          "tools": [
            { "id": "echo", "description": "Echo", "risk": "read_only",
              "command": { "executable": "echo", "arguments": ["hi"] } }
          ]
        }
        """);

        await _registry.ReloadAsync();

        Assert.Empty(_registry.Plugins);
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesStateAndReloads()
    {
        await WritePluginAsync("echo", """
        {
          "id": "echo_plugin",
          "enabled": true,
          "tools": [
            { "id": "echo", "description": "Echo", "risk": "read_only",
              "command": { "executable": "echo", "arguments": ["hi"] } }
          ]
        }
        """);
        await _registry.ReloadAsync();
        Assert.Single(_registry.Plugins);

        await _registry.SetEnabledAsync("echo_plugin", false);

        Assert.Empty(_registry.Plugins);
        Assert.True(File.Exists(Path.Combine(_root, ".state.json")));
    }

    [Fact]
    public async Task SetEnabledAsync_PersistsAcrossInstances()
    {
        // The state sidecar is the whole point of this round-trip
        // test — a new registry pointing at the same directory
        // should re-load with the user's prior toggle. Without
        // persistence, the host's "user disabled plugin X" would
        // silently un-disable on the next app launch.
        await WritePluginAsync("echo", """
        {
          "id": "echo_plugin",
          "enabled": true,
          "tools": [
            { "id": "echo", "description": "Echo", "risk": "read_only",
              "command": { "executable": "echo", "arguments": ["hi"] } }
          ]
        }
        """);
        await _registry.ReloadAsync();
        await _registry.SetEnabledAsync("echo_plugin", false);

        var secondRegistry = new PluginRegistry(_root);
        await secondRegistry.ReloadAsync();

        Assert.Empty(secondRegistry.Plugins);
    }

    [Fact]
    public async Task ReloadAsync_FiresChanged()
    {
        var fired = 0;
        _registry.Changed += (_, _) => fired++;

        await WritePluginAsync("echo", """
        {
          "id": "echo_plugin",
          "enabled": true,
          "tools": [
            { "id": "echo", "description": "Echo", "risk": "read_only",
              "command": { "executable": "echo", "arguments": ["hi"] } }
          ]
        }
        """);
        await _registry.ReloadAsync();

        Assert.True(fired >= 1, "ReloadAsync should fire Changed so the host re-binds.");
    }

    [Fact]
    public async Task ReloadAsync_SurfacesDiagnostics()
    {
        // A broken manifest (invalid JSON) must NOT crash the
        // registry — the user wants to see "this plugin didn't
        // load because…" in the diagnostics list, not have the
        // whole Plugins modal empty out.
        var pluginDir = Path.Combine(_root, "broken");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), "{ this is not json");

        await _registry.ReloadAsync();

        Assert.Empty(_registry.Plugins);
        Assert.NotEmpty(_registry.Diagnostics);
    }

    [Fact]
    public async Task PluginsDirectory_ExposesConstructorArgument()
    {
        Assert.Equal(_root, _registry.PluginsDirectory);
        await Task.CompletedTask;
    }

    private async Task WritePluginAsync(string directoryName, string manifestJson)
    {
        var pluginDir = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifestJson);
    }
}
