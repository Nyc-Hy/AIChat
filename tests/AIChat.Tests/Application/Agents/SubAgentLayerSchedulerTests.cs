using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Application.Agents;

// Unit tests for the AgentHarness scheduler. The scheduler used to
// build a topological layer DAG (cycles, diamond dependencies,
// chain dependencies) so independent sub-agents could run in
// parallel. Today the only template the coordinator schedules is
// the read-only "explorer" with no intra-batch dependencies, so
// the algorithm collapses to a single layer and the DAG
// machinery is dead — when worker / verifier templates land with
// real dependencies, the topological algorithm comes back and
// the layered tests come with it.
public class SubAgentLayerSchedulerTests
{
    [Fact]
    public void ComputeLayers_EmptyInput_ReturnsEmpty()
    {
        var layers = AgentHarness.ComputeSubAgentExecutionLayers([]);

        Assert.Empty(layers);
    }

    [Fact]
    public void ComputeLayers_SingleDecision_ReturnsSingleLayer()
    {
        var decision = NewDecision("a", order: 0);

        var layers = AgentHarness.ComputeSubAgentExecutionLayers([decision]);

        Assert.Single(layers);
        Assert.Equal(new[] { "a" }, layers[0].Select(d => d.PlannedSubAgentId));
    }

    [Fact]
    public void ComputeLayers_AllIndependent_CollapsesToSingleLayer()
    {
        // Today's real case: every scheduled sub-agent is a
        // read-only explorer with no dependency on the others, so
        // the DAG has one layer and the harness dispatches them all
        // in parallel via Task.WhenAll.
        var decisions = new[]
        {
            NewDecision("a", order: 0),
            NewDecision("b", order: 1),
            NewDecision("c", order: 2),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Single(layers);
        // Order within the layer is preserved so the activity feed
        // and event stream see the same sequence as the old
        // single-runner code.
        Assert.Equal(new[] { "a", "b", "c" }, layers[0].Select(d => d.PlannedSubAgentId));
    }

    [Fact]
    public void ComputeLayers_ExternalDependencies_AreIgnored()
    {
        // 'a' depends on 'missing', which isn't in the scheduled
        // set. The coordinator already filtered out unresolvable
        // dependencies, so this is the expected shape: 'a' is
        // ready in the first layer.
        var decisions = new[]
        {
            NewDecision("a", order: 0, dependsOn: ["missing"]),
            NewDecision("b", order: 1),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Single(layers);
        Assert.Equal(new[] { "a", "b" }, layers[0].Select(d => d.PlannedSubAgentId));
    }

    [Fact]
    public void ComputeLayers_CycleFallsBackToSingleLayer()
    {
        // Cycle: a depends on b, b depends on a. The dependency
        // checker in the coordinator shouldn't let this through,
        // but if it does the scheduler must not deadlock — it
        // dumps both into one layer so the run keeps moving.
        var decisions = new[]
        {
            NewDecision("a", order: 0, dependsOn: ["b"]),
            NewDecision("b", order: 1, dependsOn: ["a"]),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Single(layers);
        Assert.Equal(2, layers[0].Count);
    }

    private static AgentSubAgentScheduleDecision NewDecision(
        string id,
        int order,
        params string[] dependsOn)
    {
        return new AgentSubAgentScheduleDecision
        {
            Id = Guid.NewGuid().ToString("N"),
            RunId = "run",
            PlannedSubAgentId = id,
            TemplateId = "explorer",
            Phase = "gathering_context",
            Task = $"task-{id}",
            Status = "Scheduled",
            MaxToolCalls = 4,
            Order = order,
            DependsOn = dependsOn.ToList(),
            WriteScope = []
        };
    }
}
