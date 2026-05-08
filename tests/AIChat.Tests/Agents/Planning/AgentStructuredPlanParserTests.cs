using AIChat.Application.Agents.Planning;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents.Planning;

public sealed class AgentStructuredPlanParserTests
{
    [Fact]
    public void ParseOrFallback_ParsesValidPlanAndNormalizesBudgetRiskAndTools()
    {
        var parser = new AgentStructuredPlanParser();
        var request = CreateRequest();
        var json = """
        {
          "summary": "Update the planner",
          "suggestedTools": ["read_file", "missing_tool"],
          "budget": { "maxToolCalls": 99, "tokenBudget": 500 },
          "subAgents": [
            {
              "templateId": "explorer",
              "phase": "gathering-context",
              "task": "Inspect planner flow",
              "reason": "needs focused read-only context",
              "maxToolCalls": 99,
              "dependsOn": ["plan"],
              "writeScope": ["src/Nope.cs"]
            },
            {
              "templateId": "worker",
              "task": "Invalid template becomes explorer"
            }
          ],
          "phases": [
            {
              "name": "gathering-context",
              "objective": "read relevant code",
              "tasks": [
                {
                  "title": "Inspect harness",
                  "risk": "high",
                  "suggestedTools": ["read_file", "run_shell"],
                  "budget": { "maxToolCalls": 0, "tokenBudget": 999999 }
                }
              ]
            }
          ]
        }
        """;

        var plan = parser.ParseOrFallback(json, request);

        Assert.False(plan.IsFallback);
        Assert.Equal("Update the planner", plan.Summary);
        Assert.Equal(["read_file"], plan.SuggestedTools);
        Assert.Equal(20, plan.Budget.MaxToolCalls);
        Assert.Equal(1000, plan.Budget.TokenBudget);
        Assert.Equal("gathering_context", plan.Phases[0].Name);
        Assert.Equal(AgentPlanRisk.High, plan.Phases[0].Tasks[0].Risk);
        Assert.Equal(["read_file"], plan.Phases[0].Tasks[0].SuggestedTools);
        Assert.Equal(2, plan.Phases[0].Tasks[0].Budget.MaxToolCalls);
        Assert.Equal(200000, plan.Phases[0].Tasks[0].Budget.TokenBudget);
        Assert.Equal(2, plan.SubAgents.Count);
        Assert.Equal("explorer", plan.SubAgents[0].TemplateId);
        Assert.Equal("gathering_context", plan.SubAgents[0].Phase);
        Assert.Equal("Inspect planner flow", plan.SubAgents[0].Task);
        Assert.Equal(8, plan.SubAgents[0].MaxToolCalls);
        Assert.Equal(["plan"], plan.SubAgents[0].DependsOn);
        Assert.Equal(["src/Nope.cs"], plan.SubAgents[0].WriteScope);
        Assert.Equal("explorer", plan.SubAgents[1].TemplateId);
    }

    [Fact]
    public void ParseOrFallback_UsesFallbackForBadJson()
    {
        var parser = new AgentStructuredPlanParser();
        var plan = parser.ParseOrFallback("not-json", CreateRequest());

        Assert.True(plan.IsFallback);
        Assert.Single(plan.Phases);
        Assert.Single(plan.Phases[0].Tasks);
        Assert.Contains("Implement phase two", plan.Summary);
    }

    [Fact]
    public void ParseOrFallback_CapsTotalTaskCount()
    {
        var tasks = string.Join(",", Enumerable.Range(1, 20).Select(i => $$"""{"title":"Task {{i}}"}"""));
        var json = $$"""
        {
          "summary": "many tasks",
          "phases": [
            {
              "name": "executing",
              "tasks": [{{tasks}}]
            }
          ]
        }
        """;

        var plan = new AgentStructuredPlanParser().ParseOrFallback(json, CreateRequest());

        Assert.Equal(AgentStructuredPlanParser.MaxTaskCount, plan.Phases.Sum(phase => phase.Tasks.Count));
    }

    private static AgentPlanningRequest CreateRequest()
    {
        return new AgentPlanningRequest(
            "Implement phase two",
            Environment.CurrentDirectory,
            ["read_file", "apply_patch"],
            [new ChatMessage { Role = ChatRole.User, Content = "Implement phase two" }]);
    }
}
