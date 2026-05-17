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
    public async Task PluginCommandTool_RejectsWorkingDirectoryOutsidePluginAndProject()
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
                  "id": "escape",
                  "description": "Attempts to escape",
                  "risk": "read_only",
                  "command": {
                    "executable": "dotnet",
                    "arguments": ["--version"],
                    "workingDirectory": ".."
                  }
                }
              ]
            }
            """);
            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);
            var tool = Assert.Single(provider.Tools);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tool.PreviewAsync("{}", new AgentToolContext { ProjectPath = Path.Combine(root, "project") }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PluginCommandTool_RejectsRelativeExecutableOutsidePluginDirectory()
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
                  "id": "escape_executable",
                  "description": "Attempts to escape executable path",
                  "risk": "read_only",
                  "command": {
                    "executable": "../tool.exe",
                    "arguments": []
                  }
                }
              ]
            }
            """);
            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);
            var tool = Assert.Single(provider.Tools);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tool.PreviewAsync("{}", new AgentToolContext { ProjectPath = root }));
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

    [Fact]
    public async Task LoadFromDirectoryAsync_LoadsSkillsAndMcpServerDeclarations()
    {
        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "local");
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "SKILL.md"), "# Local Skill\nUse local workflow.");
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), """
            {
              "id": "local",
              "name": "Local",
              "skills": [
                {
                  "id": "workflow",
                  "name": "Workflow",
                  "path": "SKILL.md"
                }
              ],
              "mcpServers": [
                {
                  "id": "stdio_server",
                  "transport": "stdio",
                  "command": "dotnet",
                  "arguments": ["--info"]
                }
              ]
            }
            """);

            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);

            var skill = Assert.Single(provider.Skills);
            Assert.Equal("local_workflow", skill.Id);
            Assert.Contains("Use local workflow.", skill.Content);
            Assert.Equal("local_stdio_server", Assert.Single(provider.McpServers).Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_DiscoversEnabledMcpTools()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDirectory();
        try
        {
            var pluginDirectory = Path.Combine(root, "mcp");
            Directory.CreateDirectory(pluginDirectory);
            var serverScript = Path.Combine(pluginDirectory, "server.ps1");
            await File.WriteAllTextAsync(serverScript, """
            while ($line = [Console]::In.ReadLine()) {
              $msg = $line | ConvertFrom-Json
              if ($msg.method -eq 'initialize') {
                @{ jsonrpc='2.0'; id=$msg.id; result=@{ protocolVersion='2025-11-25'; capabilities=@{ tools=@{} }; serverInfo=@{ name='fake'; version='1.0' } } } | ConvertTo-Json -Depth 20 -Compress
              } elseif ($msg.method -eq 'tools/list') {
                @{ jsonrpc='2.0'; id=$msg.id; result=@{ tools=@(@{ name='echo'; description='Echo text'; inputSchema=@{ type='object'; properties=@{ text=@{ type='string' } } } }) } } | ConvertTo-Json -Depth 20 -Compress
              } elseif ($msg.method -eq 'tools/call') {
                @{ jsonrpc='2.0'; id=$msg.id; result=@{ content=@(@{ type='text'; text=('echo:' + $msg.params.arguments.text) }); isError=$false } } | ConvertTo-Json -Depth 20 -Compress
              }
            }
            """);
            await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"), $$"""
            {
              "id": "mcp",
              "name": "MCP",
              "mcpServers": [
                {
                  "id": "fake",
                  "transport": "stdio",
                  "command": "powershell.exe",
                  "arguments": ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "{{serverScript.Replace("\\", "\\\\")}}"],
                  "risk": "read_only",
                  "timeoutSeconds": 10
                }
              ]
            }
            """);

            var provider = await PluginToolProvider.LoadFromDirectoryAsync(root);

            var tool = Assert.Single(provider.Tools);
            Assert.Equal("mcp_fake_echo", tool.Id);
            Assert.Equal(AgentToolRisk.ReadOnly, tool.Risk);
            Assert.Equal("MCP", Assert.Single(provider.GetToolMetadata()).Category);

            var result = await tool.ExecuteAsync("""{"text":"hello"}""", new AgentToolContext { ProjectPath = root });
            Assert.False(result.IsError);
            Assert.Contains("echo:hello", result.Content);
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
