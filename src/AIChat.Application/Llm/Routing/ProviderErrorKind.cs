namespace AIChat.Application.Llm.Routing;

public enum ProviderErrorKind
{
    None,
    InvalidConfiguration,
    Authentication,
    PermissionDenied,
    RateLimited,
    ModelNotFound,
    ContextLengthExceeded,
    InvalidRequest,
    Network,
    Timeout,
    Server,
    Unknown
}
