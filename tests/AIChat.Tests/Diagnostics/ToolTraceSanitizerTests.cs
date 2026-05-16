using AIChat.Application.Diagnostics;

namespace AIChat.Tests.Diagnostics;

public sealed class ToolTraceSanitizerTests
{
    [Fact]
    public void SanitizeArgumentsJson_DefaultsBlankArgumentsToEmptyObject()
    {
        Assert.Equal("{}", ToolTraceSanitizer.SanitizeArgumentsJson(""));
    }

    [Fact]
    public void SanitizeArgumentsJson_RedactsSecrets()
    {
        var result = ToolTraceSanitizer.SanitizeArgumentsJson(
            """{"path":"config.txt","api_key":"sk-test-secret-value"}""");

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("sk-test-secret-value", result);
    }

    [Fact]
    public void SanitizeResultContent_RedactsSecrets()
    {
        var result = ToolTraceSanitizer.SanitizeResultContent(
            """{"stdout":"Authorization: Bearer abc.def.ghi"}""");

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("abc.def.ghi", result);
    }
}
