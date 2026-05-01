using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentVerificationViewModel : ObservableObject
{
    private readonly AgentVerification _verification;

    public AgentVerificationViewModel(AgentVerification verification)
    {
        _verification = verification;
    }

    public string Id => _verification.Id;
    public string ToolName => _verification.ToolName;
    public string Command => _verification.Command;
    public string StatusText => _verification.IsSuccess ? "通过" : _verification.TimedOut ? "超时" : "失败";
    public bool IsSuccess => _verification.IsSuccess;
    public string ExitCodeText => $"exit {_verification.ExitCode}";
    public string OutputPreview => string.IsNullOrWhiteSpace(_verification.Output)
        ? ""
        : _verification.Output.Length > 1_600 ? _verification.Output[..1_600] + "\n..." : _verification.Output;
    public string Summary => _verification.Summary;
    public bool HasSummary => !string.IsNullOrWhiteSpace(_verification.Summary);
}
