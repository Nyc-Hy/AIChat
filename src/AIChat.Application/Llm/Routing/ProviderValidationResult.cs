namespace AIChat.Application.Llm.Routing;

public sealed class ProviderValidationResult
{
    public ProviderValidationResult(IReadOnlyList<ProviderValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ProviderValidationIssue> Issues { get; }
    public bool IsValid => Issues.All(issue => issue.Severity != ProviderValidationSeverity.Error);
    public IReadOnlyList<ProviderValidationIssue> Errors => Issues.Where(issue => issue.Severity == ProviderValidationSeverity.Error).ToList();
    public IReadOnlyList<ProviderValidationIssue> Warnings => Issues.Where(issue => issue.Severity == ProviderValidationSeverity.Warning).ToList();
    public string Summary => Issues.Count == 0
        ? "配置可用。"
        : string.Join(Environment.NewLine, Issues.Select(issue => issue.Message));
}
