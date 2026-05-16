using System.Text.Json;
using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class RunBuildToolTests
{
    [Fact]
    public async Task ExecuteAsync_BuildsProjectInsideWorkspace()
    {
        using var workspace = TemporaryWorkspace.Create();
        await CreateMinimalProjectAsync(workspace.Path, valid: true);
        var tool = new RunBuildTool();

        var result = await tool.ExecuteAsync(
            """{"target":"App.csproj","timeout_seconds":60}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.False(result.IsError, result.Content);
        using var document = JsonDocument.Parse(result.Content);
        Assert.Equal(0, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains("dotnet build", document.RootElement.GetProperty("command").GetString());
        Assert.Contains("-m:1", document.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsErrorWhenBuildFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        await CreateMinimalProjectAsync(workspace.Path, valid: false);
        var tool = new RunBuildTool();

        var result = await tool.ExecuteAsync(
            """{"target":"App.csproj","timeout_seconds":60}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.NotEqual(0, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsTargetOutsideWorkspace()
    {
        using var workspace = TemporaryWorkspace.Create();
        await CreateMinimalProjectAsync(workspace.Path, valid: true);
        var tool = new RunBuildTool();

        var result = await tool.ExecuteAsync(
            """{"target":"../outside.csproj","timeout_seconds":1}""",
            new AgentToolContext { ProjectPath = workspace.Path });

        Assert.True(result.IsError);
        Assert.Contains("路径超出", result.Content);
    }

    private static async Task CreateMinimalProjectAsync(string path, bool valid)
    {
        await File.WriteAllTextAsync(
            Path.Combine(path, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(path, "Class1.cs"),
            valid
                ? "namespace App; public sealed class Class1 { public int Value => 1; }\n"
                : "namespace App; public sealed class Class1 { public int Value => ; }\n");
    }
}
