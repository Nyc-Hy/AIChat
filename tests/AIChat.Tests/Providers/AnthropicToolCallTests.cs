using System.Text;
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

    // --- helpers ---

    private static AnthropicChatProvider CreateProvider(HttpMessageHandler handler)
    {
        return new AnthropicChatProvider(new HttpClient(handler));
    }

    private static ChatRequest CreateRequest()
    {
        return new ChatRequest
        {
            Model = "claude-3-5-sonnet-latest",
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

    private static async Task<List<ChatDelta>> CollectDeltasAsync(AnthropicChatProvider provider)
    {
        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.SendAsync(CreateRequest(), CreateSettings(), CancellationToken.None))
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
}
