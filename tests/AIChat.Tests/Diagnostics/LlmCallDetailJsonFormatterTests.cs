using AIChat.Application.Diagnostics;

namespace AIChat.Tests.Diagnostics;

public sealed class LlmCallDetailJsonFormatterTests
{
    [Fact]
    public void BuildRequestSummary_CountsMessagesToolsAndContentParts()
    {
        var summary = LlmCallDetailJsonFormatter.BuildRequestSummary(
            """
            {
              "messages": [
                {
                  "contentParts": [
                    { "type": "text", "text": "hello" },
                    { "type": "image", "mimeType": "image/png" }
                  ]
                },
                {
                  "contentParts": [
                    { "type": "text", "text": "world" }
                  ]
                }
              ],
              "enabledTools": ["read_file", "git_status"]
            }
            """);

        Assert.Equal("2 条消息 · 2 个工具 · 1 张图片 · 2 个文本片段", summary);
    }

    [Fact]
    public void NormalizeJsonText_RedactsSecretsBeforeDisplay()
    {
        var normalized = LlmCallDetailJsonFormatter.NormalizeJsonText(
            """{"api_key":"sk-test-secret-value","message":"token=ghp_123456789012345678901234"}""",
            includeRawEvents: true);

        Assert.Contains("[REDACTED]", normalized);
        Assert.DoesNotContain("sk-test-secret-value", normalized);
        Assert.DoesNotContain("ghp_123456789012345678901234", normalized);
    }

    [Fact]
    public void NormalizeJsonText_SummarizesRawEventsWhenNotExpanded()
    {
        var normalized = LlmCallDetailJsonFormatter.NormalizeJsonText(
            """{"rawEvents":["{\"type\":\"delta\",\"content\":\"hi\"}","[DONE]"]}""",
            includeRawEvents: false);

        Assert.Contains("rawEventsSummary", normalized);
        Assert.Contains("\"total\": 2", normalized);
        Assert.DoesNotContain("\"rawEvents\"", normalized);
    }

    [Fact]
    public void NormalizeJsonText_LimitsExpandedRawEvents()
    {
        var events = string.Join(",", Enumerable.Range(1, 125).Select(i => $@"""{{\""index\"":{i}}}"""));
        var normalized = LlmCallDetailJsonFormatter.NormalizeJsonText(
            $$"""{"rawEvents":[{{events}}]}""",
            includeRawEvents: true);

        Assert.Contains("\"totalRawEvents\": 125", normalized);
        Assert.Contains("\"hiddenRawEvents\": 5", normalized);
        Assert.Contains("rawEvents 太多", normalized);
    }

    [Fact]
    public void NormalizeJsonText_ReturnsRedactedFallbackForInvalidJson()
    {
        var normalized = LlmCallDetailJsonFormatter.NormalizeJsonText(
            "api_key=sk-test-secret-value",
            includeRawEvents: true);

        Assert.Equal("api_key=[REDACTED]", normalized);
    }
}
