using AIChat.Application.Agents.Benchmark;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentBenchmarkResultViewModel
{
    private readonly AgentBenchmarkResult _result;

    public AgentBenchmarkResultViewModel(AgentBenchmarkResult result)
    {
        _result = result;
    }

    public string TaskId => _result.TaskId;
    public string Name => _result.Name;
    public bool Passed => _result.Passed;
    public string StatusText => _result.Passed ? "通过" : "未通过";
    public AgentRunOutcomeKind Outcome => _result.Outcome;
    public string OutcomeText => FormatOutcome(_result.Outcome);
    public string QualityScoreText => _result.QualityScore <= 0 ? "未评分" : $"{_result.QualityScore}/100";
    public string ToolCallText => $"{_result.ToolCallCount} 次";
    public string EstimatedPromptTokensText => $"{_result.EstimatedPromptTokens} tokens";
    public string Summary => _result.Summary;

    private static string FormatOutcome(AgentRunOutcomeKind outcome)
    {
        return outcome switch
        {
            AgentRunOutcomeKind.Success => "成功",
            AgentRunOutcomeKind.PartialSuccess => "部分成功",
            AgentRunOutcomeKind.Failed => "失败",
            AgentRunOutcomeKind.Cancelled => "已取消",
            AgentRunOutcomeKind.VerificationFailed => "验证失败",
            AgentRunOutcomeKind.PermissionBlocked => "权限阻塞",
            AgentRunOutcomeKind.BudgetExceeded => "预算耗尽",
            AgentRunOutcomeKind.EvidenceRisk => "证据风险",
            _ => "未知"
        };
    }
}

