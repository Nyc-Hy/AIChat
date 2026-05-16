using AIChat.Application.Diagnostics;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

// View model for the call-detail inspector. Formatting lives in Application so
// the WPF layer stays thin and the behavior is covered by ordinary unit tests.
public sealed class LlmCallDetailViewModel : ObservableObject
{
    public LlmCallDetailViewModel(LlmCallDetail detail)
    {
        Detail = detail;
    }

    public LlmCallDetail Detail { get; }
    public string Title => $"{Detail.CreatedAt.ToLocalTime():MM/dd HH:mm:ss} · {Detail.Model}";
    public string Subtitle => $"{Detail.ProviderName} · {Detail.Status}";
    public string RequestSummary => LlmCallDetailJsonFormatter.BuildRequestSummary(Detail.RequestJson);
    public bool HasRequestSummary => !string.IsNullOrWhiteSpace(RequestSummary);
    public string ResponseSummary => BuildResponseSummary();
    public string RequestJson => LlmCallDetailJsonFormatter.NormalizeJsonText(Detail.RequestJson, includeRawEvents: true);
    public string ResponseJson => LlmCallDetailJsonFormatter.NormalizeJsonText(Detail.ResponseJson, includeRawEvents: false);
    public string ResponseJsonWithRawEvents => LlmCallDetailJsonFormatter.NormalizeJsonText(Detail.ResponseJson, includeRawEvents: true);

    private string BuildResponseSummary()
    {
        var duration = Detail.CompletedAt is null
            ? "进行中"
            : FormatDuration(Detail.CompletedAt.Value - Detail.CreatedAt);
        return $"{Detail.Status} · {duration}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalSeconds < 1
            ? "<1s"
            : $"{duration.TotalSeconds:0.0}s";
    }
}
