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

    [Fact]
    public async Task LoadDirectoryWithDiagnosticsAsync_ReportsInvalidManifestAndSkipsIt()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "invalid");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "invalid",
              "tools": [
                {
                  "id": "broken",
                  "parametersJson": [],
                  "command": {}
                }
              ]
            }
            """);

            var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(root);

            Assert.Empty(result.Manifests);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == PluginDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("command.executable", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == PluginDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("parametersJson", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadDirectoryWithDiagnosticsAsync_SkipsDuplicateToolIds()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "duplicate");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "duplicate",
              "tools": [
                {
                  "id": "same",
                  "description": "first",
                  "command": { "executable": "dotnet", "arguments": ["--version"] }
                },
                {
                  "id": "same",
                  "description": "second",
                  "command": { "executable": "dotnet", "arguments": ["--version"] }
                }
              ]
            }
            """);

            var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(root);

            Assert.Empty(result.Manifests);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == PluginDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("重复", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadDirectoryWithDiagnosticsAsync_LoadsSkillsAndMcpServers()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "extended");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "SKILL.md"), "# Skill\nUse this workflow.");
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "extended",
              "skills": [
                {
                  "id": "helper",
                  "path": "SKILL.md"
                }
              ],
              "mcpServers": [
                {
                  "id": "server",
                  "transport": "stdio",
                  "command": "dotnet",
                  "arguments": ["--info"]
                }
              ]
            }
            """);

            var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(root);

            var manifest = Assert.Single(result.Manifests);
            Assert.Equal("extended_helper", Assert.Single(manifest.Skills).Id);
            Assert.Equal("extended_server", Assert.Single(manifest.McpServers).Id);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadDirectoryWithDiagnosticsAsync_RejectsSkillPathOutsidePluginDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "skill_escape");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "skill_escape",
              "skills": [
                {
                  "id": "bad",
                  "path": "../SKILL.md"
                }
              ]
            }
            """);

            var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(root);

            Assert.Empty(result.Manifests);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == PluginDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("Skill 文件必须位于插件目录内", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadDirectoryWithDiagnosticsAsync_RejectsUnsupportedMcpTransport()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "mcp");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "mcp",
              "mcpServers": [
                {
                  "id": "http_server",
                  "transport": "http",
                  "command": "server"
                }
              ]
            }
            """);

            var result = await PluginManifestLoader.LoadDirectoryWithDiagnosticsAsync(root);

            Assert.Empty(result.Manifests);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == PluginDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("stdio", StringComparison.Ordinal));
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
