using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunCheckpointSummaryBuilderTests
{
    [Fact]
    public void Build_IncludesPlanFilesErrorsAndNextStep()
    {
        var run = new AgentRun
        {
            Goal = "修复测试",
            Phase = "executing",
            MaxToolRounds = 5,
            ToolCallCount = 5,
            FinalStatusReason = "budget",
            Plan = new AgentPlan
            {
                Items =
                {
                    new AgentPlanItem { Title = "读取失败日志", Status = AgentPlanItemStatus.Completed },
                    new AgentPlanItem { Title = "修复断言", Status = AgentPlanItemStatus.Pending }
                }
            },
            Steps =
            {
                new AgentStep { Number = 1, Title = "run_test", Output = "error CS1002", IsError = true }
            },
            FileChanges =
            {
                new AgentFileChange { Path = "src/App.cs" }
            }
        };

        var summary = AgentRunCheckpointSummaryBuilder.Build(run);

        Assert.Contains("目标：修复测试", summary);
        Assert.Contains("已完成计划：读取失败日志", summary);
        Assert.Contains("未完成计划：Pending: 修复断言", summary);
        Assert.Contains("已修改文件：src/App.cs", summary);
        Assert.Contains("最近错误", summary);
        Assert.Contains("下一步建议：继续计划项：修复断言", summary);
    }
}
