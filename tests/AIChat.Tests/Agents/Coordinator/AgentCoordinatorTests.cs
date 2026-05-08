using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents.Coordinator;

public sealed class AgentCoordinatorTests
{
    [Fact]
    public void StartPhase_CompletesPreviousRunningPhase()
    {
        var coordinator = new AgentCoordinator();
        var run = new AgentRun { Id = "run-1" };

        coordinator.StartPhase(run, AgentRunPhase.Planning, "plan");
        coordinator.StartPhase(run, AgentRunPhase.GatheringContext, "read");

        Assert.Equal("gathering_context", run.Phase);
        Assert.Equal("read", run.CurrentPhaseSummary);
        Assert.Equal(2, run.PhaseHistory.Count);
        Assert.Equal("completed", run.PhaseHistory[0].Status);
        Assert.NotNull(run.PhaseHistory[0].CompletedAt);
        Assert.Equal("running", run.PhaseHistory[1].Status);
    }

    [Fact]
    public void CompleteRun_RecordsCancellationAndCompletesActivePhase()
    {
        var coordinator = new AgentCoordinator();
        var run = new AgentRun { Id = "run-1" };

        coordinator.StartPhase(run, AgentRunPhase.Executing, "edit");
        var transition = coordinator.CompleteRun(run, AgentRunStatus.Cancelled, "user stopped");

        Assert.Equal(AgentRunStatus.Cancelled, run.Status);
        Assert.Equal("cancelled", run.Phase);
        Assert.Equal("user stopped", run.CompletionReason);
        Assert.Equal("cancelled", run.PhaseHistory[0].Status);
        Assert.Equal("user stopped", run.PhaseHistory[0].Summary);
        Assert.Equal(AgentRunPhase.Cancelled, transition.Phase);
    }

    [Theory]
    [InlineData("read_file", AgentRunPhase.GatheringContext)]
    [InlineData("apply_patch", AgentRunPhase.Executing)]
    [InlineData("run_test", AgentRunPhase.Verifying)]
    [InlineData("update_plan", AgentRunPhase.Planning)]
    public void ClassifyToolPhase_ReturnsExpectedPhase(string toolName, AgentRunPhase expectedPhase)
    {
        Assert.Equal(expectedPhase, AgentCoordinator.ClassifyToolPhase(toolName));
    }

    [Fact]
    public void ShouldRunExplorer_ReturnsTrueForGatheringPlanWithContextPack()
    {
        var plan = new AgentStructuredPlan
        {
            Summary = "inspect first",
            Phases =
            [
                new AgentPlanPhase
                {
                    Name = "gathering_context",
                    Objective = "inspect files",
                    Tasks =
                    [
                        new AgentPlanTask
                        {
                            Title = "Read context",
                            SuggestedTools = ["read_file"]
                        }
                    ]
                }
            ]
        };
        var pack = new TaskContextPack
        {
            IncludedFiles = [new TaskContextFileRef { Path = "src/App.cs" }]
        };

        var shouldRun = new AgentCoordinator().ShouldRunExplorer(plan, pack, "fix app", requiresWrite: true);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunExplorer_ReturnsFalseWithoutPlanOrContextPack()
    {
        var coordinator = new AgentCoordinator();
        var plan = new AgentStructuredPlan
        {
            Phases = [new AgentPlanPhase { Name = "gathering_context" }]
        };

        Assert.False(coordinator.ShouldRunExplorer(null, new TaskContextPack(), "inspect", requiresWrite: false));
        Assert.False(coordinator.ShouldRunExplorer(plan, null, "inspect", requiresWrite: false));
    }
}
