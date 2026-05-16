namespace AIChat.Application.Llm.Routing;

public sealed record ProviderErrorInfo(
    ProviderErrorKind Kind,
    string Title,
    string Message,
    int? HttpStatusCode = null,
    bool IsTransient = false);
