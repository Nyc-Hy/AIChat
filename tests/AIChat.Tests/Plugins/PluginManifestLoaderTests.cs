using AIChat.Application.Plugins;

namespace AIChat.Tests.Plugins;

public sealed class PluginManifestLoaderTests
{
    [Fact]
    public async Task LoadDirectoryAsync_LoadsEnabledPluginManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "echo");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "Echo Plugin",
              "name": "Echo Plugin",
              "tools": [
                {
                  "id": "echo",
                  "description": "Echoes text",
                  "risk": "read_only",
                  "command": {
                    "executable": "dotnet",
                    "arguments": ["--version"]
                  }
                }
              ]
            }
            """);

            var manifests = await PluginManifestLoader.LoadDirectoryAsync(root);

            var manifest = Assert.Single(manifests);
            Assert.Equal("echo_plugin", manifest.Id);
            var tool = Assert.Single(manifest.Tools);
            Assert.Equal("echo_plugin_echo", tool.Id);
            Assert.Equal(pluginDirectory, tool.Command.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadDirectoryAsync_SkipsBrokenManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "broken");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), "{ broken json");

            var manifests = await PluginManifestLoader.LoadDirectoryAsync(root);

            Assert.Empty(manifests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AIChat-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
