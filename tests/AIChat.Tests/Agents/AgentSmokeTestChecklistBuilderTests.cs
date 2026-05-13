using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentSmokeTestChecklistBuilderTests
{
    [Fact]
    public void Build_AsksForManualReviewWhenCompletedRunChangedFiles()
    {
        var run = new AgentRun
        {
            Goal = "fix login",
            Status = AgentRunStatus.Completed,
            FileChanges =
            [
                new AgentFileChange { Path = "src/Login.cs" }
            ],
            Verifications =
            [
                new AgentVerification { IsSuccess = true }
            ]
        };

        var items = AgentSmokeTestChecklistBuilder.Build(run);

        Assert.Contains(items, item => item.Title == "确认目标完成度" && item.Status == AgentSmokeTestStatus.NeedsReview);
        Assert.Contains(items, item => item.Title == "检查变更范围" && item.Detail.Contains("src/Login.cs"));
        Assert.Contains(items, item => item.Title == "确认验证结果" && item.Status == AgentSmokeTestStatus.Passed);
    }

    [Fact]
    public void Build_BlocksWhenVerificationFailed()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Failed,
            Verifications =
            [
                new AgentVerification { IsSuccess = false, Command = "dotnet test", ExitCode = 1 }
            ]
        };

        var items = AgentSmokeTestChecklistBuilder.Build(run);

        Assert.Contains(items, item => item.Title == "确认目标完成度" && item.Status == AgentSmokeTestStatus.Blocked);
        Assert.Contains(items, item => item.Title == "确认验证结果" && item.Status == AgentSmokeTestStatus.Blocked);
    }

    [Fact]
    public void Build_PassesReadOnlyRunWithoutVerification()
    {
        var run = new AgentRun
        {
            Goal = "explain project",
            Status = AgentRunStatus.Completed
        };

        var items = AgentSmokeTestChecklistBuilder.Build(run);

        Assert.Contains(items, item => item.Title == "检查变更范围" && item.Status == AgentSmokeTestStatus.Passed);
        Assert.Contains(items, item => item.Title == "确认验证结果" && item.Status == AgentSmokeTestStatus.Passed);
    }
}
