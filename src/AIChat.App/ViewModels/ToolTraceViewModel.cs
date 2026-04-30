using System.Text.Encodings.Web;
using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class ToolTraceViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonDisplayOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

    public string ArgumentsPreview => CompactJson(_trace.ArgumentsJson, 220);
    public string ResultPreview => CompactJson(_trace.ResultContent, 700);
    public string CommandText => TryReadString(_trace.ResultContent, "command");
    public string ShellText => TryReadString(_trace.ResultContent, "shell");
    public string ExitCodeText => TryReadInt(_trace.ResultContent, "exitCode");
    public string StdoutPreview => Truncate(TryReadString(_trace.ResultContent, "stdout").ReplaceLineEndings("\n").Trim(), 700);
    public string StderrPreview => Truncate(TryReadString(_trace.ResultContent, "stderr").ReplaceLineEndings("\n").Trim(), 700);
    public bool HasResult => !string.IsNullOrWhiteSpace(_trace.ResultContent);
    public bool HasCommand => !string.IsNullOrWhiteSpace(CommandText);
    public bool HasShell => !string.IsNullOrWhiteSpace(ShellText);
    public bool HasStdout => !string.IsNullOrWhiteSpace(StdoutPreview);
    public bool HasStderr => !string.IsNullOrWhiteSpace(StderrPreview);
    public bool IsCompleted => _trace.IsCompleted;
    public bool IsError => _trace.IsError;

    public void Complete(string resultContent, bool isError)
    {
        _trace.ResultContent = resultContent;
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

    private static string CompactJson(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            normalized = JsonSerializer.Serialize(document.RootElement, JsonDisplayOptions);
        }
        catch (JsonException)
        {
            normalized = normalized.ReplaceLineEndings(" ");
        }

        return Truncate(normalized, maxLength);
    }

    private static string TryReadString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return "";
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? "",
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
                _ => JsonSerializer.Serialize(property, JsonDisplayOptions)
            };
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string TryReadInt(string json, string propertyName)
    {
        return TryReadString(json, propertyName);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
