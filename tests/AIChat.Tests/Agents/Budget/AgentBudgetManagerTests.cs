using AIChat.Application.Agents.Budget;

namespace AIChat.Tests.Agents.Budget;

public sealed class AgentBudgetManagerTests
{
    [Fact]
    public void ConsumeToolCall_StopsAtHardLimit()
    {
        var manager = new AgentBudgetManager(new AgentBudget { MaxToolCalls = 2 });

        Assert.False(manager.ConsumeToolCall("read_file").IsHardLimit);
        Assert.False(manager.ConsumeToolCall("search_text").IsHardLimit);
        var decision = manager.ConsumeToolCall("read_file");

        Assert.True(decision.ShouldPause);
        Assert.True(decision.IsHardLimit);
        Assert.Equal(AgentBudgetCheckpointType.HardLimit, decision.CheckpointType);
        Assert.Equal(2, manager.ToolCallsUsed);
    }

    [Fact]
    public void ConsumeToolCall_TriggersIntervalCheckpoint()
    {
        var manager = new AgentBudgetManager(new AgentBudget
        {
            MaxToolCalls = 10,
            ToolCheckpointInterval = 3
        });

        manager.ConsumeToolCall("read_file");
        manager.ConsumeToolCall("search_text");
        var decision = manager.ConsumeToolCall("read_file");

        Assert.True(decision.ShouldPause);
        Assert.False(decision.IsHardLimit);
        Assert.Equal(AgentBudgetCheckpointType.ToolInterval, decision.CheckpointType);
    }

    [Fact]
    public void PreviewToolCall_CheckpointsBeforeHighRiskMutation()
    {
        var manager = new AgentBudgetManager(new AgentBudget
        {
            MaxToolCalls = 10,
            PauseBeforeHighRiskMutation = true
        });

        var decision = manager.PreviewToolCall("apply_patch", isHighRiskMutation: true);

        Assert.True(decision.ShouldPause);
        Assert.Equal(AgentBudgetCheckpointType.HighRiskMutation, decision.CheckpointType);
        Assert.Equal(0, manager.ToolCallsUsed);
    }

    [Fact]
    public void RecordVerificationFailureLoop_CreatesCheckpoint()
    {
        var manager = new AgentBudgetManager(new AgentBudget
        {
            PauseAfterVerificationFailureLoop = true
        });

        var decision = manager.RecordVerificationFailureLoop();

        Assert.True(decision.ShouldPause);
        Assert.Equal(AgentBudgetCheckpointType.VerificationFailureLoop, decision.CheckpointType);
        Assert.Equal(1, manager.VerificationFailureLoops);
    }

    [Fact]
    public void ExtendToolSegment_AllowsAdditionalToolCalls()
    {
        var manager = new AgentBudgetManager(new AgentBudget { MaxToolCalls = 1 });

        manager.ConsumeToolCall("read_file");
        Assert.True(manager.ConsumeToolCall("read_file").IsHardLimit);

        manager.ExtendToolSegment(2);
        var decision = manager.ConsumeToolCall("read_file");

        Assert.False(decision.IsHardLimit);
        Assert.Equal(2, manager.ToolCallsUsed);
    }
}
