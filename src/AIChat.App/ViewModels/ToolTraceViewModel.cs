using AIChat.Application.Diagnostics;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class ToolTraceViewModel : ObservableObject
{
    private readonly ChatToolTrace _trace;

    public ToolTraceViewModel(ChatToolTrace trace)
    {
        _trace = trace;
    }

    public string Id => _trace.Id;
    public string ToolCallId => _trace.ToolCallId;
    public string ToolName => _trace.ToolName;
    public string StatusText => _trace.IsCompleted ? (_trace.IsError ? "失败" : "完成") : "运行中";
    public string DurationText
    {
        get
        {
            var end = _trace.CompletedAt ?? DateTimeOffset.Now;
            var elapsed = end - _trace.StartedAt;
            return elapsed.TotalSeconds < 1
                ? "<1s"
                : $"{elapsed.TotalSeconds:0.0}s";
        }
    }

    public string ArgumentsPreview => ToolTraceDisplayFormatter.CompactJson(_trace.ArgumentsJson, 220);
    public string ResultPreview => ToolTraceDisplayFormatter.CompactJson(_trace.ResultContent, 700);
    public string CommandText => ToolTraceDisplayFormatter.TryReadString(_trace.ResultContent, "command");
    public string ShellText => ToolTraceDisplayFormatter.TryReadString(_trace.ResultContent, "shell");
    public string ExitCodeText => ToolTraceDisplayFormatter.TryReadString(_trace.ResultContent, "exitCode");
    public string StdoutPreview => ToolTraceDisplayFormatter.Truncate(ToolTraceDisplayFormatter.TryReadString(_trace.ResultContent, "stdout").ReplaceLineEndings("\n").Trim(), 700);
    public string StderrPreview => ToolTraceDisplayFormatter.Truncate(ToolTraceDisplayFormatter.TryReadString(_trace.ResultContent, "stderr").ReplaceLineEndings("\n").Trim(), 700);
    public bool HasResult => !string.IsNullOrWhiteSpace(_trace.ResultContent);
    public bool HasCommand => !string.IsNullOrWhiteSpace(CommandText);
    public bool HasShell => !string.IsNullOrWhiteSpace(ShellText);
    public bool HasStdout => !string.IsNullOrWhiteSpace(StdoutPreview);
    public bool HasStderr => !string.IsNullOrWhiteSpace(StderrPreview);
    public bool IsCompleted => _trace.IsCompleted;
    public bool IsError => _trace.IsError;

    public string GetFullText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"工具：{ToolName}");
        sb.AppendLine($"状态：{StatusText}");
        sb.AppendLine($"耗时：{DurationText}");
        sb.AppendLine();
        sb.AppendLine("参数：");
        sb.AppendLine(ArgumentsPreview);
        if (HasResult)
        {
            sb.AppendLine();
            sb.AppendLine("结果：");
            if (HasShell)
            {
                sb.AppendLine($"Shell: {ShellText}  Exit: {ExitCodeText}");
                if (HasCommand) sb.AppendLine($"命令：{CommandText}");
                if (HasStdout) sb.AppendLine($"stdout：\n{StdoutPreview}");
                if (HasStderr) sb.AppendLine($"stderr：\n{StderrPreview}");
            }
            else
            {
                sb.AppendLine(ResultPreview);
            }
        }
        return sb.ToString();
    }

    public void Complete(string resultContent, bool isError)
    {
        _trace.ResultContent = ToolTraceSanitizer.SanitizeResultContent(resultContent);
        _trace.IsError = isError;
        _trace.IsCompleted = true;
        _trace.CompletedAt = DateTimeOffset.Now;
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResultPreview));
        OnPropertyChanged(nameof(CommandText));
        OnPropertyChanged(nameof(ShellText));
        OnPropertyChanged(nameof(ExitCodeText));
        OnPropertyChanged(nameof(StdoutPreview));
        OnPropertyChanged(nameof(StderrPreview));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasCommand));
        OnPropertyChanged(nameof(HasShell));
        OnPropertyChanged(nameof(HasStdout));
        OnPropertyChanged(nameof(HasStderr));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsError));
    }

}
