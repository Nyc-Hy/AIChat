using System.Text.Json;
using AIChat.Application.Plugins;
using AIChat.Application.Tools;

namespace AIChat.Tests.Plugins;

public sealed class PluginToolProviderTests
{
    [Fact]
    public async Task LoadFromDirectoryAsync_CreatesToolsAndMetadata()
    {
        var root = CreateTempDirectory();
        try
        {
            await WritePluginAsync(root, """
            {
              "id": "local",
              "name": "Local",
              "tools": [
                {
                  "id": "dotnet_version",
                  "description": "Reads the installed dotnet version",
                  "risk": "read_only",
                  "category": "插件",
                  "groupLabel": "本地插件",
                  "command": {
                    "executable": "dotnet",
                    "arguments": ["--version"],
                    "timeoutSeconds": 30
                  }
                }
              ]
            }
            """);

            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);

            var tool = Assert.Single(provider.Tools);
            Assert.Equal("local_dotnet_version", tool.Id);
            Assert.Equal(AgentToolRisk.ReadOnly, tool.Risk);
            var metadata = Assert.Single(provider.GetToolMetadata());
            Assert.Equal(tool.Id, metadata.ToolId);
            Assert.Equal("本地插件", metadata.GroupLabel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PluginCommandTool_ExecutesConfiguredCommand()
    {
        var root = CreateTempDirectory();
        try
        {
            await WritePluginAsync(root, """
            {
              "id": "local",
              "name": "Local",
              "tools": [
                {
                  "id": "dotnet_version",
                  "description": "Reads the installed dotnet version",
                  "risk": "read_only",
                  "command": {
                    "executable": "dotnet",
                    "arguments": ["--version"],
                    "timeoutSeconds": 30
                  }
                }
              ]
            }
            """);
            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);
            var tool = Assert.Single(provider.Tools);

            var result = await tool.ExecuteAsync("{}", new AgentToolContext { ProjectPath = root });

            Assert.False(result.IsError);
            using var document = JsonDocument.Parse(result.Content);
            Assert.Equal("local", document.RootElement.GetProperty("plugin").GetString());
            Assert.Contains(".", document.RootElement.GetProperty("output").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AgentToolRegistry_LoadsPluginToolsWithMetadata()
    {
        var root = CreateTempDirectory();
        try
        {
            await WritePluginAsync(root, """
            {
              "id": "local",
              "name": "Local",
              "tools": [
                {
                  "id": "dotnet_version",
                  "description": "Reads the installed dotnet version",
                  "risk": "read_only",
                  "command": {
                    "executable": "dotnet",
                    "arguments": ["--version"]
                  }
                }
              ]
            }
            """);

            var registry = await AgentToolRegistry.CreateDefaultWithPluginsAsync(root);

            Assert.NotNull(registry.Find("local_dotnet_version"));
            Assert.Equal("插件", registry.GetMetadata("local_dotnet_version").Category);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WritePluginAsync(string root, string content)
    {
        var pluginDirectory = Path.Combine(root, "local");
        Directory.CreateDirectory(pluginDirectory);
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), content);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AIChat-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
