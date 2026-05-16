namespace AIChat.Application.Llm.Routing;

public sealed record ProviderConnectionTestResult(
    bool IsSuccess,
    ProviderErrorKind ErrorKind,
    string Message,
    int? HttpStatusCode = null)
{
    public static ProviderConnectionTestResult Success(string message)
    {
        return new ProviderConnectionTestResult(true, ProviderErrorKind.None, message);
    }

    public static ProviderConnectionTestResult Failure(ProviderErrorInfo error)
    {
        return new ProviderConnectionTestResult(false, error.Kind, error.Message, error.HttpStatusCode);
    }
}
