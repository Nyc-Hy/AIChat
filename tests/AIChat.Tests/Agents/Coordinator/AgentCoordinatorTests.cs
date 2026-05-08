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

    [Fact]
    public void SelectPlannedSubAgents_ReturnsRunnableExplorerPlansBeforeFallback()
    {
        var plan = new AgentStructuredPlan
        {
            SubAgents =
            [
                new AgentPlannedSubAgent
                {
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "Inspect auth flow",
                    Order = 2
                },
                new AgentPlannedSubAgent
                {
                    TemplateId = "verifier",
                    Phase = "verifying",
                    Task = "Run checks",
                    Order = 1
                },
                new AgentPlannedSubAgent
                {
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "Would write",
                    WriteScope = ["src/App.cs"],
                    Order = 0
                }
            ]
        };

        var selected = new AgentCoordinator().SelectPlannedSubAgents(
            plan,
            new TaskContextPack { IncludedFiles = [new TaskContextFileRef { Path = "src/Auth.cs" }] },
            "fix auth",
            requiresWrite: true);

        var agent = Assert.Single(selected);
        Assert.Equal("Inspect auth flow", agent.Task);
    }

    [Fact]
    public void CreateSubAgentSchedule_RecordsSkippedDecisions()
    {
        var plan = new AgentStructuredPlan
        {
            SubAgents =
            [
                new AgentPlannedSubAgent
                {
                    Id = "agent-a",
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "Inspect auth flow",
                    Order = 0
                },
                new AgentPlannedSubAgent
                {
                    Id = "agent-b",
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "Inspect auth flow",
                    Order = 1
                },
                new AgentPlannedSubAgent
                {
                    Id = "agent-c",
                    TemplateId = "explorer",
                    Phase = "executing",
                    Task = "Late inspect",
                    Order = 2
                },
                new AgentPlannedSubAgent
                {
                    Id = "agent-d",
                    TemplateId = "explorer",
                    Phase = "gathering_context",
                    Task = "Blocked inspect",
                    DependsOn = ["missing-agent"],
                    Order = 3
                },
                new AgentPlannedSubAgent
                {
                    Id = "agent-e",
                    TemplateId = "worker",
                    Phase = "gathering_context",
                    Task = "Unsupported worker",
                    Order = 4
                }
            ]
        };

        var decisions = new AgentCoordinator().CreateSubAgentSchedule(
            "run-1",
            plan,
            new TaskContextPack { IncludedFiles = [new TaskContextFileRef { Path = "src/Auth.cs" }] },
            "fix auth",
            requiresWrite: true);

        Assert.Equal(5, decisions.Count);
        Assert.Equal("Scheduled", decisions[0].Status);
        Assert.All(decisions.Skip(1), decision => Assert.Equal("Skipped", decision.Status));
        Assert.Contains("Duplicate", decisions[1].SkipReason);
        Assert.Contains("not runnable", decisions[2].SkipReason);
        Assert.Contains("dependencies", decisions[3].SkipReason);
        Assert.Contains("Unsupported", decisions[4].SkipReason);
    }
}
