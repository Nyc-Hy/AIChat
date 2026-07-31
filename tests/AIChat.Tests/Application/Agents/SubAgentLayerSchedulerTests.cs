using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Application.Agents;

// Unit tests for the AgentHarness DAG-to-waves scheduler. The
// scheduler is a static method so the tests can poke the layer
// boundaries directly without spinning up the full harness. The
// scheduler's job is to return layers such that every decision's
// dependencies are in an earlier layer (or in no layer at all, e.g.
// skipped by the coordinator).
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
    public void ComputeLayers_ChainDependencies_ProducesLinearLayers()
    {
        // a → b → c
        var decisions = new[]
        {
            NewDecision("a", order: 0, dependsOn: []),
            NewDecision("b", order: 1, dependsOn: ["a"]),
            NewDecision("c", order: 2, dependsOn: ["b"]),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Equal(3, layers.Count);
        Assert.Equal(new[] { "a" }, layers[0].Select(d => d.PlannedSubAgentId));
        Assert.Equal(new[] { "b" }, layers[1].Select(d => d.PlannedSubAgentId));
        Assert.Equal(new[] { "c" }, layers[2].Select(d => d.PlannedSubAgentId));
    }

    [Fact]
    public void ComputeLayers_DiamondDependency_ProducesTwoLayers()
    {
        // a → {b, c} → d
        var decisions = new[]
        {
            NewDecision("a", order: 0),
            NewDecision("b", order: 1, dependsOn: ["a"]),
            NewDecision("c", order: 2, dependsOn: ["a"]),
            NewDecision("d", order: 3, dependsOn: ["b", "c"]),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Equal(3, layers.Count);
        Assert.Equal(new[] { "a" }, layers[0].Select(d => d.PlannedSubAgentId));
        Assert.Equal(new[] { "b", "c" }, layers[1].Select(d => d.PlannedSubAgentId));
        Assert.Equal(new[] { "d" }, layers[2].Select(d => d.PlannedSubAgentId));
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

    [Fact]
    public void ComputeLayers_MixedDagAndIndependent_HandlesLayersCorrectly()
    {
        // 'a' and 'b' are independent (run in layer 0).
        // 'c' depends on 'a' (layer 1). 'd' is independent of
        // everything but is in layer 1 because c is. Actually,
        // since 'd' has no deps, it could run in layer 0 with a
        // and b — verify the algorithm picks the earliest legal
        // layer.
        var decisions = new[]
        {
            NewDecision("a", order: 0),
            NewDecision("b", order: 1),
            NewDecision("c", order: 2, dependsOn: ["a"]),
            NewDecision("d", order: 3),
        };

        var layers = AgentHarness.ComputeSubAgentExecutionLayers(decisions);

        Assert.Equal(2, layers.Count);
        // 'a', 'b', 'd' are all dependency-free → layer 0.
        Assert.Equal(new[] { "a", "b", "d" }, layers[0].Select(d => d.PlannedSubAgentId));
        // 'c' depends on 'a' → layer 1.
        Assert.Equal(new[] { "c" }, layers[1].Select(d => d.PlannedSubAgentId));
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
