using System.Net;
using System.Text;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Domain.Chat;
using AIChat.Providers.OpenAI;

namespace AIChat.Tests.Providers;

// 2026-08-05: pin the token-usage parsing in the
// OpenAI-compatible streaming adapter. The new
// stream_options.include_usage flag tells the platform
// to attach a usage block to the final chunk; without
// it the provider can't surface the cache hit rate in
// the activity feed. The shape:
//   { "id": "…", "model": "…", "choices": [],
//     "usage": { "prompt_tokens": 177,
//                "completion_tokens": 5,
//                "total_tokens": 182,
//                "prompt_tokens_details": {
//                  "cached_tokens": 114
//                } } }
public class OpenAICompatibleChatProviderUsageTests
{
    [Fact]
    public async Task SendAsync_RequestsStreamOptionsIncludeUsage()
    {
        var captured = new CapturingHandler("data: [DONE]\n\n");
        var provider = new OpenAICompatibleChatProvider(new HttpClient(captured));

        await ConsumeAsync(provider);

        Assert.NotNull(captured.CapturedBody);
        // The provider must send stream_options.include_usage
        // so the platform actually attaches the usage block.
        // A future refactor that drops this flag (e.g. a
        // "simplify the payload" cleanup) would break the
        // prompt-cache display in the activity feed.
        Assert.Contains("\"stream_options\"", captured.CapturedBody!);
        Assert.Contains("\"include_usage\"", captured.CapturedBody!);
        Assert.Contains("true", captured.CapturedBody!);
    }

    [Fact]
    public async Task SendAsync_UsageOnlyChunk_PopulatesUsage()
    {
        // The MiniMax streaming response emits the
        // usage block in a separate chunk between the
        // last content delta and [DONE] (choices is
        // empty on the usage chunk). The provider
        // should surface this as a ChatDelta with
        // Usage populated but Content/ReasoningContent/
        // ToolCalls empty — the runner reads the Usage
        // field and updates the activity feed.
        var usageChunk = "data: {\"id\":\"x\",\"model\":\"MiniMax-M3\",\"choices\":[],"
            + "\"usage\":{\"prompt_tokens\":177,\"completion_tokens\":5,\"total_tokens\":182,"
            + "\"prompt_tokens_details\":{\"cached_tokens\":114}}}\n\n";
        var doneChunk = "data: [DONE]\n\n";
        var provider = new OpenAICompatibleChatProvider(
            new HttpClient(new StubHandler(BuildSseBody(usageChunk, doneChunk))));

        var deltas = await ConsumeAsync(provider);

        // Find the delta that carries the usage block.
        // The current implementation yields it as a
        // stand-alone delta (no content); the runner
        // reads agentEvent.Usage off it.
        var withUsage = deltas.Where(d => d.Usage is not null).ToList();
        Assert.NotEmpty(withUsage);
        var usage = withUsage.Last().Usage!;
        Assert.Equal(177, usage.PromptTokens);
        Assert.Equal(5, usage.CompletionTokens);
        Assert.Equal(114, usage.CachedTokens);
        Assert.Equal(182, usage.TotalTokens);
        Assert.Equal(64.4, usage.CacheHitPercent);
    }

    [Fact]
    public async Task SendAsync_NoUsageBlock_LeavesUsageNull()
    {
        // Legacy providers (or curl probes that
        // forget stream_options) won't include the
        // usage block. The provider must leave the
        // ChatDelta.Usage null in that case so the
        // runner's null-check on LastRunUsage works
        // correctly (no NRE, no garbage 0/0/0).
        var contentChunk = "data: {\"id\":\"x\",\"model\":\"x\",\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n";
        var doneChunk = "data: [DONE]\n\n";
        var provider = new OpenAICompatibleChatProvider(
            new HttpClient(new StubHandler(BuildSseBody(contentChunk, doneChunk))));

        var deltas = await ConsumeAsync(provider);

        Assert.All(deltas, d => Assert.Null(d.Usage));
    }

    // Drive the IAsyncEnumerable SendAsync to completion and
    // collect every delta. The harness consumes the same
    // shape downstream so this is the realistic shape test.
    private static async Task<List<ChatDelta>> ConsumeAsync(OpenAICompatibleChatProvider provider)
    {
        var request = new ChatRequest
        {
            Model = "MiniMax-M3",
            Temperature = 0.3,
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "hi" }]
        };
        var settings = new AppSettings
        {
            ProviderId = "minimax",
            ApiKey = "test-key",
            BaseUrl = "https://example.com",
            Model = "MiniMax-M3"
        };

        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.SendAsync(request, settings))
        {
            deltas.Add(delta);
        }
        return deltas;
    }

    private static byte[] BuildSseBody(params string[] events)
    {
        var sb = new StringBuilder();
        foreach (var evt in events)
        {
            sb.Append(evt);
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public StubHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(_body))
            });
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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(_responseBody)))
            };
        }
    }
}
