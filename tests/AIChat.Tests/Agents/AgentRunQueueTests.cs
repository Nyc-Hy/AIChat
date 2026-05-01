using AIChat.Application.Agents;

namespace AIChat.Tests.Agents;

public sealed class AgentRunQueueTests
{
    [Fact]
    public void IsRunning_FalseByDefault()
    {
        var queue = new AgentRunQueue();
        Assert.False(queue.IsRunning);
        Assert.Null(queue.CurrentRunId);
    }

    [Fact]
    public void TryStart_SucceedsWhenIdle()
    {
        var queue = new AgentRunQueue();
        Assert.True(queue.TryStart("run-1"));
        Assert.True(queue.IsRunning);
        Assert.Equal("run-1", queue.CurrentRunId);
    }

    [Fact]
    public void TryStart_FailsWhenAlreadyRunning()
    {
        var queue = new AgentRunQueue();
        Assert.True(queue.TryStart("run-1"));
        Assert.False(queue.TryStart("run-2"));
        Assert.Equal("run-1", queue.CurrentRunId);
    }

    [Fact]
    public void Complete_ClearsCurrentRun()
    {
        var queue = new AgentRunQueue();
        queue.TryStart("run-1");
        queue.Complete("run-1");
        Assert.False(queue.IsRunning);
        Assert.Null(queue.CurrentRunId);
    }

    [Fact]
    public void Complete_IgnoresMismatchedRunId()
    {
        var queue = new AgentRunQueue();
        queue.TryStart("run-1");
        queue.Complete("run-2");
        Assert.True(queue.IsRunning);
        Assert.Equal("run-1", queue.CurrentRunId);
    }

    [Fact]
    public void TryStart_SucceedsAfterComplete()
    {
        var queue = new AgentRunQueue();
        queue.TryStart("run-1");
        queue.Complete("run-1");
        Assert.True(queue.TryStart("run-2"));
        Assert.Equal("run-2", queue.CurrentRunId);
    }

    [Fact]
    public void Complete_IsIdempotent()
    {
        var queue = new AgentRunQueue();
        queue.TryStart("run-1");
        queue.Complete("run-1");
        queue.Complete("run-1");
        Assert.False(queue.IsRunning);
    }
}
