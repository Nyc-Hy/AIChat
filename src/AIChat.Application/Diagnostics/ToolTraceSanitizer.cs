using AIChat.Application.Security;

namespace AIChat.Application.Diagnostics;

public static class ToolTraceSanitizer
{
    public static string SanitizeArgumentsJson(string argumentsJson)
    {
        return string.IsNullOrWhiteSpace(argumentsJson)
            ? "{}"
            : SensitiveDataRedactor.RedactText(argumentsJson);
    }

    public static string SanitizeResultContent(string resultContent)
    {
        return string.IsNullOrWhiteSpace(resultContent)
            ? ""
            : SensitiveDataRedactor.RedactText(resultContent);
    }
}
