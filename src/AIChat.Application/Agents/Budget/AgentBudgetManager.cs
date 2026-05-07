namespace AIChat.Application.Agents.Budget;

public sealed class AgentBudgetManager
{
    private readonly DateTimeOffset _startedAt;
    private int _toolSegmentLimit;

    public AgentBudgetManager(AgentBudget budget, DateTimeOffset? startedAt = null)
    {
        Budget = budget;
        _startedAt = startedAt ?? DateTimeOffset.Now;
        _toolSegmentLimit = Math.Max(0, budget.MaxToolCalls);
    }

    public AgentBudget Budget { get; }
    public int ToolCallsUsed { get; private set; }
    public int ModelTokensUsed { get; private set; }
    public int VerificationFailureLoops { get; private set; }

    public AgentBudgetDecision PreviewToolCall(string toolName, bool isHighRiskMutation)
    {
        if (_toolSegmentLimit > 0 && ToolCallsUsed >= _toolSegmentLimit)
        {
            return AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.HardLimit,
                $"工具调用预算已耗尽：{ToolCallsUsed}/{_toolSegmentLimit}",
                isHardLimit: true);
        }

        if (Budget.PauseBeforeHighRiskMutation && isHighRiskMutation)
        {
            return AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.HighRiskMutation,
                $"即将执行高风险工具：{toolName}");
        }

        return CheckElapsedTime();
    }

    public AgentBudgetDecision ConsumeToolCall(string toolName, bool isHighRiskMutation = false, bool allowCheckpointPause = true)
    {
        var preview = PreviewToolCall(toolName, isHighRiskMutation);
        if (preview.IsHardLimit)
        {
            return preview;
        }

        ToolCallsUsed++;

        if (allowCheckpointPause &&
            Budget.ToolCheckpointInterval > 0 &&
            ToolCallsUsed % Budget.ToolCheckpointInterval == 0)
        {
            return AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.ToolInterval,
                $"已执行 {ToolCallsUsed} 次工具调用，建议 checkpoint。");
        }

        return AgentBudgetDecision.Continue();
    }

    public AgentBudgetDecision ConsumeModelTokens(int tokens)
    {
        ModelTokensUsed += Math.Max(0, tokens);
        if (Budget.MaxModelTokens > 0 && ModelTokensUsed >= Budget.MaxModelTokens)
        {
            return AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.HardLimit,
                $"模型 token 预算已耗尽：{ModelTokensUsed}/{Budget.MaxModelTokens}",
                isHardLimit: true);
        }

        return CheckElapsedTime();
    }

    public AgentBudgetDecision RecordVerificationFailureLoop()
    {
        VerificationFailureLoops++;
        if (Budget.PauseAfterVerificationFailureLoop)
        {
            return AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.VerificationFailureLoop,
                $"验证失败修复循环已发生 {VerificationFailureLoops} 次。");
        }

        return AgentBudgetDecision.Continue();
    }

    public void ExtendToolSegment(int additionalToolCalls)
    {
        if (additionalToolCalls <= 0)
        {
            return;
        }

        _toolSegmentLimit = _toolSegmentLimit <= 0
            ? ToolCallsUsed + additionalToolCalls
            : _toolSegmentLimit + additionalToolCalls;
    }

    private AgentBudgetDecision CheckElapsedTime()
    {
        if (Budget.MaxElapsedTime is null)
        {
            return AgentBudgetDecision.Continue();
        }

        var elapsed = DateTimeOffset.Now - _startedAt;
        return elapsed >= Budget.MaxElapsedTime
            ? AgentBudgetDecision.Pause(
                AgentBudgetCheckpointType.HardLimit,
                $"运行时间预算已耗尽：{elapsed.TotalSeconds:0.0}s",
                isHardLimit: true)
            : AgentBudgetDecision.Continue();
    }
}
