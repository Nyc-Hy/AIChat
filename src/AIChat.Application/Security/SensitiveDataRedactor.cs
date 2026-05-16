using System.Text.RegularExpressions;

namespace AIChat.Application.Security;

public static class SensitiveDataRedactor
{
    public const string RedactedValue = "[REDACTED]";

    private static readonly string[] SensitiveKeyFragments =
    [
        "api_key",
        "apikey",
        "authorization",
        "bearer",
        "password",
        "secret",
        "token"
    ];

    private static readonly Regex JsonSecretRegex = new(
        "(\"(?:[a-z0-9_-]*api[_-]?key|apikey|authorization|password|secret|token)\"\\s*:\\s*\")([^\"]+)(\")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AssignmentSecretRegex = new(
        "\\b([A-Za-z0-9_-]*api[_-]?key|apikey|authorization|password|secret|token)\\s*=\\s*([^\\s;,&]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerRegex = new(
        "\\bBearer\\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommonTokenRegex = new(
        "\\b(?:sk-[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,}|xox[baprs]-[A-Za-z0-9-]{20,})\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RedactText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = JsonSecretRegex.Replace(value, $"$1{RedactedValue}$3");
        redacted = AssignmentSecretRegex.Replace(redacted, match => $"{match.Groups[1].Value}={RedactedValue}");
        redacted = BearerRegex.Replace(redacted, $"Bearer {RedactedValue}");
        redacted = CommonTokenRegex.Replace(redacted, RedactedValue);
        return redacted;
    }

    public static IReadOnlyDictionary<string, string> RedactDictionary(IReadOnlyDictionary<string, string> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => IsSensitiveKey(pair.Key) ? RedactedValue : RedactText(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return SensitiveKeyFragments.Any(fragment =>
            normalized.Contains(fragment.Replace("_", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
    }
}
