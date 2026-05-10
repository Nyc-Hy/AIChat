using System.Text;
using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.Chat;
using AIChat.Providers.OpenAI;

namespace AIChat.Tests.Providers;

public sealed class OpenAICompatibleToolCallTests
{
    [Fact]
    public async Task SendAsync_ParsesSingleToolCall()
    {
        var sse = BuildSseStream(
            DeltaChunk(0, "call-1", "read_file", index: 0, argsPart: "{\"path\":\"src/Program.cs\"}"),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("{\"path\":\"src/Program.cs\"}", toolCalls[0].ArgumentsJson);
        Assert.Equal("call-1", toolCalls[0].Id);
    }

    [Fact]
    public async Task SendAsync_ParsesMultipleToolCallsInOneChunk()
    {
        var sse = BuildSseStream(
            MultiToolDeltaChunk(
                ("call-1", "read_file", 0, "{\"path\":\"a.cs\"}"),
                ("call-2", "write_file", 1, "{\"path\":\"b.cs\",\"content\":\"hello\"}")),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Equal(2, toolCalls.Count);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("write_file", toolCalls[1].Name);
    }

    [Fact]
    public async Task SendAsync_EmptyToolCallsArray_ProducesNoToolCalls()
    {
        var sse = BuildSseStream(
            ContentDeltaChunk("Hello"),
            EmptyToolCallsChunk(),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Empty(toolCalls);
    }

    [Fact]
    public async Task SendAsync_ToolCallWithMissingArguments_DefaultsToEmptyObject()
    {
        var sse = BuildSseStream(
            DeltaChunk(0, "call-1", "list_files", index: 0, argsPart: null),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Single(toolCalls);
        Assert.Equal("list_files", toolCalls[0].Name);
        Assert.Equal("{}", toolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task SendAsync_StreamedToolCallDeltas_AccumulatesCorrectly()
    {
        // Simulate a streamed tool call where name and args arrive in chunks
        var sse = BuildSseStream(
            DeltaChunk(0, "call-1", "rea", index: 0, argsPart: "{\"pa"),
            DeltaChunk(0, null, "d_file", index: 0, argsPart: "th\":\"src"),
            DeltaChunk(0, null, null, index: 0, argsPart: "/Program.cs\"}"),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Single(toolCalls);
        Assert.Equal("read_file", toolCalls[0].Name);
        Assert.Equal("{\"path\":\"src/Program.cs\"}", toolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task SendAsync_BrokenToolParametersJson_FallsBackToEmptyObjectSchema()
    {
        var handler = new CapturingHandler(ContentDeltaChunk("hi") + DoneChunk());
        var provider = new OpenAICompatibleChatProvider(new HttpClient(handler));

        var request = new ChatRequest
        {
            Model = "gpt-4",
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
        var settings = CreateSettings();

        await CollectDeltasAsync(provider, request, settings);

        var captured = System.Text.Json.JsonDocument.Parse(handler.CapturedBody!);
        var tool = captured.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("parameters");

        Assert.Equal("object", tool.GetProperty("type").GetString());
        Assert.True(tool.TryGetProperty("properties", out var props));
        Assert.Equal(JsonValueKind.Object, props.ValueKind);
    }

    [Fact]
    public async Task SendAsync_ToolCallIdFallback_UsesIndexWhenIdEmpty()
    {
        var sse = BuildSseStream(
            DeltaChunk(0, "", "read_file", index: 0, argsPart: "{}"),
            DoneChunk());

        var provider = CreateProvider(sse);
        var request = CreateRequest();
        var settings = CreateSettings();

        var deltas = await CollectDeltasAsync(provider, request, settings);
        var toolCalls = deltas.SelectMany(d => d.ToolCalls).ToList();

        Assert.Single(toolCalls);
        Assert.Equal("tool-0", toolCalls[0].Id);
    }

    [Fact]
    public async Task SendAsync_UserMessageWithImagePart_SendsOpenAIImageContentBlock()
    {
        var handler = new CapturingHandler(ContentDeltaChunk("ok") + DoneChunk());
        var provider = new OpenAICompatibleChatProvider(new HttpClient(handler));
        var request = new ChatRequest
        {
            Model = "gpt-4.1-mini",
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

        await CollectDeltasAsync(provider, request, CreateSettings());

        using var captured = JsonDocument.Parse(handler.CapturedBody!);
        var content = captured.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("describe this screenshot", content[0].GetProperty("text").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,AQIDBA==", content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    // --- helpers ---

    private static OpenAICompatibleChatProvider CreateProvider(HttpMessageHandler handler)
    {
        return new OpenAICompatibleChatProvider(new HttpClient(handler));
    }

    private static ChatRequest CreateRequest()
    {
        return new ChatRequest
        {
            Model = "gpt-4",
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
            BaseUrl = "https://fake.api/v1",
            ProviderName = "OpenAI Compatible",
            ProviderId = "openai",
            ProtocolId = "openai"
        };
    }

    private static async Task<List<ChatDelta>> CollectDeltasAsync(
        OpenAICompatibleChatProvider provider,
        ChatRequest request,
        AppSettings settings)
    {
        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.SendAsync(request, settings, CancellationToken.None))
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

    private static string DeltaChunk(int choiceIndex, string? id, string? name, int index, string? argsPart)
    {
        var function = new Dictionary<string, object?>();
        if (name is not null) function["name"] = name;
        if (argsPart is not null) function["arguments"] = argsPart;

        var toolCall = new Dictionary<string, object?>
        {
            ["index"] = index
        };
        if (id is not null) toolCall["id"] = id;
        if (function.Count > 0) toolCall["function"] = function;

        var delta = new Dictionary<string, object?>
        {
            ["tool_calls"] = new[] { toolCall }
        };

        var choice = new Dictionary<string, object?>
        {
            ["index"] = choiceIndex,
            ["delta"] = delta
        };

        var payload = new Dictionary<string, object?>
        {
            ["choices"] = new[] { choice }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string MultiToolDeltaChunk(params (string id, string name, int index, string args)[] tools)
    {
        var toolCalls = tools.Select(t => new Dictionary<string, object?>
        {
            ["id"] = t.id,
            ["index"] = t.index,
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = t.name,
                ["arguments"] = t.args
            }
        }).ToArray();

        var delta = new Dictionary<string, object?>
        {
            ["tool_calls"] = toolCalls
        };

        var choice = new Dictionary<string, object?>
        {
            ["index"] = 0,
            ["delta"] = delta
        };

        var payload = new Dictionary<string, object?>
        {
            ["choices"] = new[] { choice }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string ContentDeltaChunk(string content)
    {
        var payload = new Dictionary<string, object?>
        {
            ["choices"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["index"] = 0,
                    ["delta"] = new Dictionary<string, object?> { ["content"] = content }
                }
            }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string EmptyToolCallsChunk()
    {
        var payload = new Dictionary<string, object?>
        {
            ["choices"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["index"] = 0,
                    ["delta"] = new Dictionary<string, object?> { ["tool_calls"] = Array.Empty<object>() }
                }
            }
        };

        return $"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string DoneChunk() => "data: [DONE]\n\n";

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
