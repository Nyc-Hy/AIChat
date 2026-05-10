using AIChat.Application.Projects;

namespace AIChat.Tests.Projects;

public sealed class ProjectInitializerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AIChatProjectInitTests", Guid.NewGuid().ToString("N"));

    public ProjectInitializerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void SuggestVerificationCommands_ForDotnetSolutionCreatesBuildAndTestCommands()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Demo.sln"), "");

        var commands = new ProjectInitializer().SuggestVerificationCommands(_tempDir);

        Assert.Equal(2, commands.Count);
        Assert.Equal("构建", commands[0].Name);
        Assert.Equal("dotnet build", commands[0].Command);
        Assert.Equal("Demo.sln", commands[0].WorkingDirectory);
        Assert.Equal(120, commands[0].TimeoutSeconds);
        Assert.True(commands[0].IsDefault);
        Assert.Equal("测试", commands[1].Name);
        Assert.Equal("dotnet test", commands[1].Command);
        Assert.Equal("Demo.sln", commands[1].WorkingDirectory);
        Assert.Equal(180, commands[1].TimeoutSeconds);
        Assert.True(commands[1].IsDefault);
    }

    [Fact]
    public void SuggestVerificationCommands_ForUnknownProjectReturnsEmptyList()
    {
        var commands = new ProjectInitializer().SuggestVerificationCommands(_tempDir);

        Assert.Empty(commands);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
