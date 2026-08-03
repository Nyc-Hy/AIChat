using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Projects;
using AIChat.Tests.TestDoubles;
using Moq;

namespace AIChat.Tests.Avalonia;

public sealed class AgentHostViewModelTests : IDisposable
{
    private readonly string _projectPath = Path.Combine(
        Path.GetTempPath(),
        "AIChatAgentHostTests",
        Guid.NewGuid().ToString("N"));

    public AgentHostViewModelTests() => Directory.CreateDirectory(_projectPath);

    [Fact]
    public void CanRunVerification_TracksProjectAndRunState()
    {
        var (host, sidebar, _, _) = CreateHost();
        Assert.False(host.CanRunVerification);

        sidebar.Refresh([CreateProject()]);
        Assert.True(host.CanRunVerification);

        host.IsRunning = true;
        Assert.False(host.CanRunVerification);
        host.IsRunning = false;
        Assert.True(host.CanRunVerification);

        host.IsVerifying = true;
        Assert.False(host.CanRunVerification);
        host.IsVerifying = false;
        sidebar.Refresh([]);
        Assert.False(host.CanRunVerification);
    }

    [Fact]
    public async Task RunVerification_BlockedCommandSurfacesFailureAndResetsState()
    {
        var (host, sidebar, activity, toast) = CreateHost();
        sidebar.Refresh([CreateProject()]);

        await host.RunVerificationCommand.ExecuteAsync(null);

        Assert.False(host.IsVerifying);
        var result = Assert.Single(activity.Activity, item => item.Title == "验证：unsafe");
        Assert.Equal("失败", result.Status);
        Assert.Contains(toast.Toasts, item =>
            item.Level == ToastLevel.Warning && item.Message.Contains("部分验证失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportConversationPersistenceFailure_AddsDurableFeedback()
    {
        var (host, _, activity, toast) = CreateHost();

        await host.ReportConversationPersistenceFailureAsync();

        var item = Assert.Single(activity.Activity);
        Assert.Equal("对话保存失败", item.Title);
        Assert.Equal("警告", item.Status);
        Assert.Contains(toast.Toasts, toastItem => toastItem.Level == ToastLevel.Warning);
    }

    [Fact]
    public void ContextBudgetDetails_LabelsEstimateAndAvoidsBillingClaim()
    {
        var (host, _, _, _) = CreateHost();
        host.InputTokens = 250_000;

        Assert.Contains("25%", host.ContextBudgetDetails);
        Assert.Contains("本地路由估算", host.ContextBudgetDetails);
        Assert.Contains("不是提供方计费 usage", host.ContextBudgetDetails);
    }

    [Fact]
    public void PrepareContinuation_ClearsComposerForNewInstruction()
    {
        var (host, _, _, _) = CreateHost();
        host.DraftPrompt = "old draft";

        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "original goal",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed
        });

        Assert.Equal("", host.DraftPrompt);
    }

    private (AgentHostViewModel Host, ProjectSidebarViewModel Sidebar, ActivityFeedViewModel Activity, ToastService Toast)
        CreateHost()
    {
        var repository = new InMemoryAppRepository();
        var settingsHolder = new SettingsHolder();
        settingsHolder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, settingsHolder);
        var activity = new ActivityFeedViewModel();
        var toast = new ToastService(action => action());
        var host = new AgentHostViewModel(
            Mock.Of<IChatCompletionService>(),
            AgentToolRegistry.CreateForTests([]),
            Mock.Of<IApprovalService>(),
            repository,
            sidebar,
            new ConversationListViewModel(repository),
            activity,
            toast,
            new InMemorySourceRegistry(),
            _ => { },
            () => settingsHolder.Current,
            () => false,
            () => false,
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        return (host, sidebar, activity, toast);
    }

    private WorkspaceProject CreateProject() => new()
    {
        Id = "project-1",
        Name = "Project",
        Folders = [new WorkspaceFolder { Id = "primary-1", Path = _projectPath }],
        PrimaryFolderId = "primary-1",
        VerificationCommands =
        [
            new ProjectVerificationCommand
            {
                Id = "unsafe",
                Name = "unsafe",
                Command = "rm -rf /"
            }
        ]
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectPath, recursive: true);
        }
        catch
        {
        }
    }
}
