namespace AIChat.Application.Llm.Routing;

public sealed record ProviderErrorInfo(
    ProviderErrorKind Kind,
    string Title,
    string Message,
    int? HttpStatusCode = null,
    bool IsTransient = false,
    // 2026-08-04: actionable next-step the user can take
    // without leaving the Settings modal. The previous
    // shape only surfaced the error title + raw response
    // body, which for the 401-on-M3-with-Coding-Plan
    // case read as "API Key 无效或缺失" with no hint
    // that the key was actually valid — just on the
    // wrong billing tier. Setting this lets the Settings
    // modal (and the toast surface) render a one-line
    // "what to do next" instead of a dead-end error.
    // Empty when the error has no actionable fix (e.g.
    // a transient 5xx) — the caller renders just the
    // Title + Message in that case.
    string RemediationHint = "");
