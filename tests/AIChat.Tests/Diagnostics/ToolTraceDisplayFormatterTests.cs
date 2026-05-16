using AIChat.Application.Diagnostics;

namespace AIChat.Tests.Diagnostics;

public sealed class ToolTraceDisplayFormatterTests
{
    [Fact]
    public void CompactJson_MinifiesValidJson()
    {
        var result = ToolTraceDisplayFormatter.CompactJson(
            """
            {
              "command": "git status",
              "exitCode": 0
            }
            """,
            maxLength: 200);

        Assert.Equal("""{"command":"git status","exitCode":0}""", result);
    }

    [Fact]
    public void CompactJson_FallsBackToSingleLineText()
    {
        var result = ToolTraceDisplayFormatter.CompactJson("line1\r\nline2", maxLength: 200);

        Assert.Equal("line1 line2", result);
    }

    [Fact]
    public void CompactJson_RedactsSecrets()
    {
        var result = ToolTraceDisplayFormatter.CompactJson(
            """{"api_key":"sk-test-secret-value","stdout":"token=ghp_123456789012345678901234"}""",
            maxLength: 200);

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("sk-test-secret-value", result);
        Assert.DoesNotContain("ghp_123456789012345678901234", result);
    }

    [Fact]
    public void TryReadString_ReadsShellResultValues()
    {
        var json = """{"command":"dotnet test","exitCode":1,"timedOut":false,"details":{"a":1}}""";

        Assert.Equal("dotnet test", ToolTraceDisplayFormatter.TryReadString(json, "command"));
        Assert.Equal("1", ToolTraceDisplayFormatter.TryReadString(json, "exitCode"));
        Assert.Equal("False", ToolTraceDisplayFormatter.TryReadString(json, "timedOut"));
        Assert.Equal("""{"a":1}""", ToolTraceDisplayFormatter.TryReadString(json, "details"));
    }

    [Fact]
    public void TryReadString_ReturnsEmptyForInvalidJson()
    {
        Assert.Equal("", ToolTraceDisplayFormatter.TryReadString("not json", "command"));
    }

    [Fact]
    public void TryReadString_RedactsSecrets()
    {
        var result = ToolTraceDisplayFormatter.TryReadString(
            """{"stdout":"Authorization: Bearer abc.def.ghi"}""",
            "stdout");

        Assert.Equal("Authorization: Bearer [REDACTED]", result);
    }

    [Fact]
    public void Truncate_AppendsEllipsis()
    {
        Assert.Equal("abc...", ToolTraceDisplayFormatter.Truncate("abcdef", maxLength: 3));
    }
}
