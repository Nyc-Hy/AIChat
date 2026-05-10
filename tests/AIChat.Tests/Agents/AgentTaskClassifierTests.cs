using AIChat.Application.Agents;

namespace AIChat.Tests.Agents;

public sealed class AgentTaskClassifierTests
{
    [Fact]
    public void Classify_TreatsExplanationAsSimple()
    {
        var result = new AgentTaskClassifier().Classify(
            "解释这个类是怎么工作的",
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory });

        Assert.Equal(AgentTaskComplexity.Simple, result);
    }

    [Fact]
    public void Classify_TreatsMutationAsStandard()
    {
        var result = new AgentTaskClassifier().Classify(
            "修复登录失败的问题",
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory });

        Assert.Equal(AgentTaskComplexity.Standard, result);
    }

    [Fact]
    public void Classify_TreatsArchitectureWorkAsComplex()
    {
        var result = new AgentTaskClassifier().Classify(
            "做一个跨模块架构重构方案并实现",
            new AgentRunContext { ProjectPath = Environment.CurrentDirectory });

        Assert.Equal(AgentTaskComplexity.Complex, result);
    }
}
