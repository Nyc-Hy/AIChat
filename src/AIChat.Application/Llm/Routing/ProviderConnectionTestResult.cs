namespace AIChat.Application.Llm.Routing;

public sealed record ProviderConnectionTestResult(
    bool IsSuccess,
    ProviderErrorKind ErrorKind,
    string Message,
    int? HttpStatusCode = null,
    // 2026-08-04: full ProviderErrorInfo on
    // failure so the Settings modal can read
    // RemediationHint. The previous shape
    // flattened Kind + Message, which made the
    // "actionable next step" unreachable from the
    // UI layer. Null on success.
    ProviderErrorInfo? ErrorInfo = null)
{
    public static ProviderConnectionTestResult Success(string message)
    {
        return new ProviderConnectionTestResult(true, ProviderErrorKind.None, message);
    }

    public static ProviderConnectionTestResult Failure(ProviderErrorInfo error)
    {
        return new ProviderConnectionTestResult(false, error.Kind, error.Message, error.HttpStatusCode, error);
    }
}
