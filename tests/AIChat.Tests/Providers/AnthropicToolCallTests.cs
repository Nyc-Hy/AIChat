using System.Text;
using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.Chat;
using AIChat.Providers.Anthropic;

namespace AIChat.Tests.Providers;

public sealed class AnthropicToolCallTests
{
    [Fact]
    public async Task SendAsync_ParsesSingleToolUse()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "tool_use", "toolu_01", "read_file"),
            InputJsonDeltaEvent(0, "{\"path\":\"src/Program.cs\"}"),
            ContentBlockStopEvent(0),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("{\"path\":\"src/Program.cs\"}", toolCalls[0].ArgumentsJson);
        Assert.Equal("toolu_01", toolCalls[0].Id);
    }

    [Fact]
    public async Task SendAsync_ParsesMultipleToolUseBlocks()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "tool_use", "toolu_01", "read_file"),
            InputJsonDeltaEvent(0, "{\"path\":\"a.cs\"}"),
            ContentBlockStopEvent(0),
            ContentBlockStartEvent(1, "tool_use", "toolu_02", "write_file"),
            InputJsonDeltaEvent(1, "{\"path\":\"b.cs\",\"content\":\"hello\"}"),
            ContentBlockStopEvent(1),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Equal(2, toolCalls.Count);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("toolu_01", toolCalls[0].Id);
        Assert.Equal("write_file", toolCalls[1].Name);
        Assert.Equal("toolu_02", toolCalls[1].Id);
    }

    [Fact]
    public async Task SendAsync_MixedTextAndToolUse_SurfacesBoth()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "text", null, null),
            TextDeltaEvent(0, "I'll read the file."),
            ContentBlockStopEvent(0),
            ContentBlockStartEvent(1, "tool_use", "toolu_01", "read_file"),
            InputJsonDeltaEvent(1, "{\"path\":\"src/Program.cs\"}"),
            ContentBlockStopEvent(1),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var textContent = string.Concat(deltas.Select(d => d.Content));
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Contains("I'll read the file.", textContent);
        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
    }

    [Fact]
    public async Task SendAsync_ToolUseWithEmptyInput_DefaultsToEmptyObject()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "tool_use", "toolu_01", "list_files"),
            // No input_json_delta events — input is empty.
            ContentBlockStopEvent(0),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Single(toolCalls);
        Assert.Equal("list_files", toolCalls[0].Name);
        Assert.Equal("{}", toolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task SendAsync_ToolUseWithStreamedInputDeltas_AccumulatesCorrectly()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "tool_use", "toolu_01", "read_file"),
            InputJsonDeltaEvent(0, "{\"pa"),
            InputJsonDeltaEvent(0, "th\":\"src"),
            InputJsonDeltaEvent(0, "/Program.cs\"}"),
            ContentBlockStopEvent(0),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("{\"path\":\"src/Program.cs\"}", toolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task SendAsync_MalformedJson_DoesNotCrash()
    {
        var sse = BuildSseStream(
            "data: {not valid json\n\n",
            ContentBlockStartEvent(0, "tool_use", "toolu_01", "read_file"),
            InputJsonDeltaEvent(0, "{\"path\":\"a.cs\"}"),
            ContentBlockStopEvent(0),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        // Should still parse the valid tool call after the malformed line.
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
    }

    [Fact]
    public async Task SendAsync_WithTools_IncludesToolsInRequestPayload()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "test" }],
            Tools =
            [
                new ChatToolDefinition
                {
                    Name = "read_file",
                    Description = "Read a file from disk",
                    ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}"
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var root = captured.RootElement;

        Assert.True(root.TryGetProperty("tools", out var tools));
        Assert.Equal(1, tools.GetArrayLength());

        var tool = tools[0];
        Assert.Equal("read_file", tool.GetProperty("name").GetString());
        Assert.Equal("Read a file from disk", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendAsync_BrokenToolParametersJson_FallsBackToEmptyObjectSchema()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "test" }],
            Tools =
            [
                new ChatToolDefinition
                {
                    Name = "broken_tool",
                    Description = "has bad parameters",
                    ParametersJson = "{broken"
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var tool = captured.RootElement.GetProperty("tools")[0];

        Assert.Equal("broken_tool", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.True(tool.GetProperty("input_schema").TryGetProperty("properties", out var props));
        Assert.Equal(JsonValueKind.Object, props.ValueKind);
    }

    [Fact]
    public async Task SendAsync_WithoutTools_OmitsToolsFromRequestPayload()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "test" }],
            Tools = []
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var root = captured.RootElement;

        Assert.False(root.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task SendAsync_MultipleTools_IncludesAllInRequest()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "test" }],
            Tools =
            [
                new ChatToolDefinition
                {
                    Name = "read_file",
                    Description = "Read a file",
                    ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}"
                },
                new ChatToolDefinition
                {
                    Name = "write_file",
                    Description = "Write a file",
                    ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}}}"
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var tools = captured.RootElement.GetProperty("tools");

        Assert.Equal(2, tools.GetArrayLength());
        Assert.Equal("read_file", tools[0].GetProperty("name").GetString());
        Assert.Equal("write_file", tools[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendAsync_ToolUseIdFallback_UsesIndexWhenIdMissing()
    {
        var sse = BuildSseStream(
            ContentBlockStartEvent(0, "tool_use", null, "list_files"),
            InputJsonDeltaEvent(0, "{}"),
            ContentBlockStopEvent(0),
            MessageStopEvent("end_turn"));

        var provider = CreateProvider(sse);
        var deltas = await CollectDeltasAsync(provider);

        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();
        Assert.Single(toolCalls);
        Assert.Equal("tool-0", toolCalls[0].Id);
    }

    [Fact]
    public async Task SendAsync_AssistantToolCalls_SerializedAsToolUseBlocks()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "read the file" },
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "I'll read it.",
                    ToolCalls =
                    [
                        new ChatToolCall { Id = "toolu_01", Name = "read_file", ArgumentsJson = "{\"path\":\"a.cs\"}" }
                    ]
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var messages = captured.RootElement.GetProperty("messages");

        // First message: plain user
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("read the file", messages[0].GetProperty("content").GetString());

        // Second message: assistant with content blocks
        var assistantMsg = messages[1];
        Assert.Equal("assistant", assistantMsg.GetProperty("role").GetString());
        var content = assistantMsg.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("I'll read it.", content[0].GetProperty("text").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_01", content[1].GetProperty("id").GetString());
        Assert.Equal("read_file", content[1].GetProperty("name").GetString());
        Assert.Equal("a.cs", content[1].GetProperty("input").GetProperty("path").GetString());
    }

    [Fact]
    public async Task SendAsync_ToolResult_SerializedAsToolResultBlock()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "read the file" },
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "",
                    ToolCalls = [new ChatToolCall { Id = "toolu_01", Name = "read_file", ArgumentsJson = "{\"path\":\"a.cs\"}" }]
                },
                new ChatMessage
                {
                    Role = ChatRole.Tool,
                    Content = "file contents here",
                    ToolCallId = "toolu_01",
                    ToolName = "read_file"
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var messages = captured.RootElement.GetProperty("messages");

        // Third message: user with tool_result block
        var toolResultMsg = messages[2];
        Assert.Equal("user", toolResultMsg.GetProperty("role").GetString());
        var content = toolResultMsg.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_01", content[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("file contents here", content[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_MultipleToolResults_MergedIntoOneMessage()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "read two files" },
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "",
                    ToolCalls =
                    [
                        new ChatToolCall { Id = "toolu_01", Name = "read_file", ArgumentsJson = "{\"path\":\"a.cs\"}" },
                        new ChatToolCall { Id = "toolu_02", Name = "read_file", ArgumentsJson = "{\"path\":\"b.cs\"}" }
                    ]
                },
                new ChatMessage { Role = ChatRole.Tool, Content = "contents of a", ToolCallId = "toolu_01" },
                new ChatMessage { Role = ChatRole.Tool, Content = "contents of b", ToolCallId = "toolu_02" }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var messages = captured.RootElement.GetProperty("messages");

        // 3 messages total: user, assistant, user (merged tool results)
        Assert.Equal(3, messages.GetArrayLength());

        // Assistant message: 2 tool_use blocks
        var assistantContent = messages[1].GetProperty("content");
        Assert.Equal(2, assistantContent.GetArrayLength());
        Assert.Equal("tool_use", assistantContent[0].GetProperty("type").GetString());
        Assert.Equal("toolu_01", assistantContent[0].GetProperty("id").GetString());
        Assert.Equal("tool_use", assistantContent[1].GetProperty("type").GetString());
        Assert.Equal("toolu_02", assistantContent[1].GetProperty("id").GetString());

        // Tool results: merged into single user message with 2 tool_result blocks
        var toolResultMsg = messages[2];
        Assert.Equal("user", toolResultMsg.GetProperty("role").GetString());
        var toolContent = toolResultMsg.GetProperty("content");
        Assert.Equal(2, toolContent.GetArrayLength());
        Assert.Equal("tool_result", toolContent[0].GetProperty("type").GetString());
        Assert.Equal("toolu_01", toolContent[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("contents of a", toolContent[0].GetProperty("content").GetString());
        Assert.Equal("tool_result", toolContent[1].GetProperty("type").GetString());
        Assert.Equal("toolu_02", toolContent[1].GetProperty("tool_use_id").GetString());
        Assert.Equal("contents of b", toolContent[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_PlainChat_NoToolBlocks()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "hello" },
                new ChatMessage { Role = ChatRole.Assistant, Content = "hi there" }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var messages = captured.RootElement.GetProperty("messages");

        // Both messages should be plain strings, not content block arrays
        Assert.Equal("hello", messages[0].GetProperty("content").GetString());
        Assert.Equal("hi there", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_UserMessageWithImagePart_SendsAnthropicImageContentBlock()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = "describe this screenshot",
                    ContentParts =
                    [
                        ChatContentPart.ImagePart("image/png", "AQIDBA==", "screen.png")
                    ]
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        using var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var content = captured.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("describe this screenshot", content[0].GetProperty("text").GetString());
        Assert.Equal("image", content[1].GetProperty("type").GetString());
        var source = content[1].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal("AQIDBA==", source.GetProperty("data").GetString());
    }

    [Fact]
    public async Task SendAsync_BadToolArguments_FallsBackToEmptyObject()
    {
        var handler = new CapturingHandler(MessageStopEvent("end_turn"));
        var provider = new AnthropicChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "do something" },
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "",
                    ToolCalls = [new ChatToolCall { Id = "toolu_01", Name = "read_file", ArgumentsJson = "{not valid json" }]
                }
            ]
        };

        await CollectDeltasAsync(provider, request);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var assistantContent = captured.RootElement.GetProperty("messages")[1].GetProperty("content");

        // Should not crash; tool_use block should have empty object input
        Assert.Equal(1, assistantContent.GetArrayLength());
        Assert.Equal("tool_use", assistantContent[0].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, assistantContent[0].GetProperty("input").ValueKind);
    }

    [Fact]
    public void ParseJsonSafe_EmptyString_ReturnsEmptyObject()
    {
        var result = AnthropicChatProvider.ParseJsonSafe("");
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public void ParseJsonSafe_InvalidJson_ReturnsEmptyObject()
    {
        var result = AnthropicChatProvider.ParseJsonSafe("{broken");
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public void ParseJsonSafe_ValidJson_ParsesCorrectly()
    {
        var result = AnthropicChatProvider.ParseJsonSafe("{\"path\":\"a.cs\"}");
        Assert.Equal("a.cs", result.GetProperty("path").GetString());
    }

    // --- helpers ---

    private static AnthropicChatProvider CreateProvider(HttpMessageHandler handler)
    {
        return new AnthropicChatProvider(new HttpClient(handler));
    }

    private static ChatRequest CreateRequest()
    {
        return new ChatRequest
        {
            Model = "claude-opus-4-6",
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "test" }
            ]
        };
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            ApiKey = "test-key",
            BaseUrl = "https://fake.api",
            ProviderName = "Anthropic",
            ProviderId = "anthropic",
            ProtocolId = "anthropic"
        };
    }

    private static async Task<List<ChatDelta>> CollectDeltasAsync(AnthropicChatProvider provider, ChatRequest? request = null)
    {
        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.SendAsync(request ?? CreateRequest(), CreateSettings(), CancellationToken.None))
        {
            deltas.Add(delta);
        }

        return deltas;
    }

    private static HttpMessageHandler BuildSseStream(params string[] events)
    {
        var sb = new StringBuilder();
        foreach (var evt in events)
        {
            sb.Append(evt);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new StubHandler(bytes);
    }

    private static string ContentBlockStartEvent(int index, string type, string? id, string? name)
    {
        var block = new Dictionary<string, object?> { ["type"] = type };
        if (id is not null) block["id"] = id;
        if (name is not null) block["name"] = name;
        if (type == "tool_use") block["input"] = new { };

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "content_block_start",
            ["index"] = index,
            ["content_block"] = block
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string InputJsonDeltaEvent(int index, string partialJson)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "content_block_delta",
            ["index"] = index,
            ["delta"] = new Dictionary<string, object?>
            {
                ["type"] = "input_json_delta",
                ["partial_json"] = partialJson
            }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string TextDeltaEvent(int index, string text)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "content_block_delta",
            ["index"] = index,
            ["delta"] = new Dictionary<string, object?>
            {
                ["type"] = "text_delta",
                ["text"] = text
            }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string ContentBlockStopEvent(int index)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "content_block_stop",
            ["index"] = index
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string MessageStopEvent(string stopReason)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "message_delta",
            ["delta"] = new Dictionary<string, object?>
            {
                ["stop_reason"] = stopReason
            }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public StubHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(_body))
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public string? CapturedBody { get; private set; }

        public CapturingHandler(string responseBody) => _responseBody = responseBody;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var bytes = Encoding.UTF8.GetBytes(_responseBody);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(bytes))
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return response;
        }
    }
}
