using AIChat.Application.Verification;
using AIChat.Application.Security;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Verification;

public sealed class ProjectVerificationExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIChatVerify_" + Guid.NewGuid().ToString("N"));

    public ProjectVerificationExecutorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RunAsync_ExecutesAllowlistedCommandAndBlocksUnsafeCommand()
    {
        var results = await new ProjectVerificationExecutor().RunAsync(_root,
        [
            new ProjectVerificationCommand { Name = "Echo", Command = "echo verification-ok", TimeoutSeconds = 30 },
            new ProjectVerificationCommand { Name = "Unsafe", Command = "rm -rf /", TimeoutSeconds = 30 }
        ]);

        Assert.True(results[0].IsSuccess);
        Assert.Equal("echo verification-ok", results[0].Command);
        Assert.Contains("verification-ok", results[0].Output);
        Assert.False(results[1].IsSuccess);
        Assert.Contains("安全允许列表", results[1].Output);
        Assert.Equal("验证命令已阻止", results[1].Summary);
    }

    [Fact]
    public async Task RunAsync_HonorsCancellationBeforeStartingCommand()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProjectVerificationExecutor().RunAsync(
                _root,
                [new ProjectVerificationCommand { Name = "Echo", Command = "echo never" }],
                cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_ReportsNonZeroExitCodeAsFailure()
    {
        var result = Assert.Single(await new ProjectVerificationExecutor().RunAsync(_root,
        [
            new ProjectVerificationCommand
            {
                Name = "Invalid dotnet option",
                Command = "dotnet --definitely-invalid",
                TimeoutSeconds = 30
            }
        ]));

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task RunAsync_UsesConfiguredWorkingDirectory()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var command = OperatingSystem.IsWindows() ? "echo %CD%" : "pwd";

        var result = Assert.Single(await new ProjectVerificationExecutor().RunAsync(_root,
        [
            new ProjectVerificationCommand
            {
                Name = "Working directory",
                Command = command,
                WorkingDirectory = "nested",
                TimeoutSeconds = 30
            }
        ]));

        Assert.True(result.IsSuccess);
        Assert.Contains("nested", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_RedactsSensitiveOutput()
    {
        const string secret = "sk-1234567890abcdefghijkl";
        var result = Assert.Single(await new ProjectVerificationExecutor().RunAsync(_root,
        [
            new ProjectVerificationCommand
            {
                Name = "Sensitive output",
                Command = $"echo Bearer {secret}",
                TimeoutSeconds = 30
            }
        ]));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataRedactor.RedactedValue, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReportsTimeoutAsFailure()
    {
        var slowProject = Path.Combine(_root, "slow");
        Directory.CreateDirectory(slowProject);
        await File.WriteAllTextAsync(
            Path.Combine(slowProject, "Slow.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(
            Path.Combine(slowProject, "Program.cs"),
            "System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(10));");

        var result = Assert.Single(await new ProjectVerificationExecutor().RunAsync(_root,
        [
            new ProjectVerificationCommand
            {
                Name = "Timeout",
                Command = "dotnet run --project Slow.csproj",
                WorkingDirectory = "slow",
                TimeoutSeconds = 1
            }
        ]));

        Assert.False(result.IsSuccess);
        Assert.Contains("超时", result.Output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
