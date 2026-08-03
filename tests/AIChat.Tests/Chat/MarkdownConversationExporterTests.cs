using AIChat.Application.Chat;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Chat;

public sealed class MarkdownConversationExporterTests
{
    [Fact]
    public void Export_TitleAndHeader_OnEverySession()
    {
        var md = MarkdownConversationExporter.Export(new Standalone
        {
            Title = "Daily driver 启动排查",
            UpdatedAt = DateTimeOffset.Parse("2026-08-01T09:00:00+08:00"),
        });

        Assert.Contains("# Daily driver 启动排查", md);
        Assert.Contains("Last updated: 2026-08-01", md);
        Assert.Contains("Messages: 0", md);
    }

    [Fact]
    public void Export_EmptySession_ShowsPlaceholder()
    {
        var md = MarkdownConversationExporter.Export(new Standalone { Title = "空" });
        Assert.Contains("_(此对话还没有任何消息)_", md);
    }

    [Fact]
    public void Export_UserMessage_RenderedAsUserSection()
    {
        var session = new Standalone
        {
            Title = "t",
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = "为什么 keychain 一直弹窗?",
                    CreatedAt = DateTimeOffset.Parse("2026-08-03T09:00:00+08:00"),
                },
            ],
        };

        var md = MarkdownConversationExporter.Export(session);

        Assert.Contains("## 用户 · #1 ·", md);
        Assert.Contains("为什么 keychain 一直弹窗?", md);
    }

    [Fact]
    public void Export_AssistantMessageWithToolTrace_RendersCallAndResult()
    {
        var session = new Standalone
        {
            Title = "t",
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "我先看 settings.json。",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ToolTraces =
                    [
                        new ChatToolTrace
                        {
                            ToolName = "read_file",
                            ArgumentsJson = "{\"path\":\"settings.json\"}",
                            ResultContent = "{\"providerId\":\"minimax\"}",
                        },
                    ],
                },
            ],
        };

        var md = MarkdownConversationExporter.Export(session);

        Assert.Contains("## 助手 ·", md);
        Assert.Contains("我先看 settings.json。", md);
        Assert.Contains("### 工具调用 · read_file", md);
        Assert.Contains("```json", md);
        Assert.Contains("\"path\":\"settings.json\"", md);
        Assert.Contains("**结果**", md);
        Assert.Contains("\"providerId\":\"minimax\"", md);
    }

    [Fact]
    public void Export_ErrorMessage_AddsWarningBanner()
    {
        var session = new Standalone
        {
            Title = "t",
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "模型 5xx 了。",
                    IsError = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            ],
        };

        var md = MarkdownConversationExporter.Export(session);

        Assert.Contains("> ⚠️ 这条消息产生过错误。", md);
        Assert.Contains("模型 5xx 了。", md);
    }

    [Fact]
    public void Export_PreservesOrderAndNumbering()
    {
        var session = new Standalone
        {
            Title = "t",
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "first", CreatedAt = DateTimeOffset.UtcNow },
                new ChatMessage { Role = ChatRole.Assistant, Content = "second", CreatedAt = DateTimeOffset.UtcNow },
                new ChatMessage { Role = ChatRole.User, Content = "third", CreatedAt = DateTimeOffset.UtcNow },
            ],
        };

        var md = MarkdownConversationExporter.Export(session);

        var firstIdx = md.IndexOf("#1 ·", StringComparison.Ordinal);
        var secondIdx = md.IndexOf("#2 ·", StringComparison.Ordinal);
        var thirdIdx = md.IndexOf("#3 ·", StringComparison.Ordinal);
        Assert.True(firstIdx >= 0 && secondIdx > firstIdx && thirdIdx > secondIdx,
            "messages should be numbered 1..N in storage order");
    }
}
