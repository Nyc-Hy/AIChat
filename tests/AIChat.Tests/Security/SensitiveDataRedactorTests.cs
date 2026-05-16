using AIChat.Application.Security;

namespace AIChat.Tests.Security;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void RedactText_RedactsCommonSecretShapes()
    {
        var value = """
        {"api_key":"sk-test-secret-value","Authorization":"Bearer abc.def.ghi"}
        {"x-api-key":"anthropic-secret-value"}
        token=ghp_123456789012345678901234
        openai_api_key=sk-proj-testsecretvalue123
        github_pat_11ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz012345
        """;

        var redacted = SensitiveDataRedactor.RedactText(value);

        Assert.DoesNotContain("sk-test-secret-value", redacted);
        Assert.DoesNotContain("abc.def.ghi", redacted);
        Assert.DoesNotContain("anthropic-secret-value", redacted);
        Assert.DoesNotContain("ghp_123456789012345678901234", redacted);
        Assert.DoesNotContain("sk-proj-testsecretvalue123", redacted);
        Assert.DoesNotContain("github_pat_11ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz012345", redacted);
        Assert.Contains(SensitiveDataRedactor.RedactedValue, redacted);
    }

    [Fact]
    public void RedactDictionary_RedactsSensitiveKeysAndSecretValues()
    {
        var redacted = SensitiveDataRedactor.RedactDictionary(new Dictionary<string, string>
        {
            ["api_key"] = "secret",
            ["safe"] = "Bearer token-value",
            ["mode"] = "fast"
        });

        Assert.Equal(SensitiveDataRedactor.RedactedValue, redacted["api_key"]);
        Assert.Equal($"Bearer {SensitiveDataRedactor.RedactedValue}", redacted["safe"]);
        Assert.Equal("fast", redacted["mode"]);
    }
}
