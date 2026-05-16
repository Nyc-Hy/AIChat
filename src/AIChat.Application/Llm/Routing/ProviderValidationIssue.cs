namespace AIChat.Application.Llm.Routing;

public sealed record ProviderValidationIssue(
    ProviderValidationSeverity Severity,
    string Code,
    string Message);
