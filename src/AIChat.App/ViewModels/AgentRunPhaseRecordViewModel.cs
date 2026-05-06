using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentRunPhaseRecordViewModel
{
    private readonly AgentRunPhaseRecord _record;

    public AgentRunPhaseRecordViewModel(AgentRunPhaseRecord record)
    {
        _record = record;
    }

    public string Id => _record.Id;
    public string PhaseText => _record.Phase switch
    {
        "planning" => "规划",
        "gathering_context" => "收集上下文",
        "executing" => "执行",
        "verifying" => "验证",
        "repairing" => "修复",
        "summarizing" => "总结",
        "waiting_for_user" => "等待用户",
        "completed" => "完成",
        "cancelled" => "已停止",
        "failed" => "失败",
        _ => "执行中"
    };
    public string StatusText => _record.Status switch
    {
        "running" => "进行中",
        "completed" => "完成",
        "cancelled" => "已停止",
        "failed" => "失败",
        _ => _record.Status
    };
    public string Summary => string.IsNullOrWhiteSpace(_record.Summary) ? "无摘要" : _record.Summary;
    public string StartedText => _record.StartedAt.ToLocalTime().ToString("HH:mm:ss");
    public string DurationText
    {
        get
        {
            var end = _record.CompletedAt ?? DateTimeOffset.Now;
            var elapsed = end - _record.StartedAt;
            return elapsed.TotalSeconds < 1 ? "<1s" : $"{elapsed.TotalSeconds:0.0}s";
        }
    }
}
