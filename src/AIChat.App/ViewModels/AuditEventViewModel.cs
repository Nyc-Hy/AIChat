using AIChat.Domain.Audit;

namespace AIChat.App.ViewModels;

public sealed class AuditEventViewModel
{
    private readonly AuditEvent? _event;

    public AuditEventViewModel(AuditEvent? auditEvent)
    {
        _event = auditEvent;
    }

    public string TimestampText => _event?.Timestamp.ToLocalTime().ToString("HH:mm:ss") ?? "";
    public string TypeText
    {
        get
        {
            if (_event == null) return "未知";
            return _event.Type switch
            {
                AuditEventType.ToolCallRequested => "工具调用请求",
                AuditEventType.ToolCallApproved => "工具已批准",
                AuditEventType.ToolCallRejected => "工具已拒绝",
                AuditEventType.ToolCallSessionAllowed => "本会话允许",
                AuditEventType.FileWritten => "文件写入",
                AuditEventType.ShellExecuted => "Shell 执行",
                AuditEventType.RollbackPerformed => "回滚操作",
                AuditEventType.VerificationRun => "验证运行",
                AuditEventType.AgentRunStarted => "运行开始",
                AuditEventType.AgentRunCompleted => "运行完成",
                AuditEventType.AgentRunFailed => "运行失败",
                AuditEventType.AgentRunCancelled => "运行取消",
                _ => "未知"
            };
        }
    }
    public string ToolName => _event?.ToolName ?? "";
    public string Summary => _event?.Summary ?? "";
    public bool HasToolName => !string.IsNullOrWhiteSpace(_event?.ToolName);
}
