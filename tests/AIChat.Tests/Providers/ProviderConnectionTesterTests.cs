using System.Net;
using System.Text;
using AIChat.Abstractions.Llm;
using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Providers;

public sealed class ProviderConnectionTesterTests
{
    [Fact]
    public async Task TestAsync_ReturnsSuccessForOkModelsResponse()
    {
        var tester = new ProviderConnectionTester(new HttpClient(new StubHandler(HttpStatusCode.OK, "{}")));

        var result = await tester.TestAsync(CreateProvider());

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderErrorKind.None, result.ErrorKind);
    }

    [Fact]
    public async Task TestAsync_ClassifiesUnauthorizedResponse()
    {
        var tester = new ProviderConnectionTester(new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, "bad key")));

        var result = await tester.TestAsync(CreateProvider());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorKind.Authentication, result.ErrorKind);
        Assert.Equal(401, result.HttpStatusCode);
    }

    [Fact]
    public async Task TestAsync_ReturnsInvalidConfigurationBeforeNetwork()
    {
        var tester = new ProviderConnectionTester(new HttpClient(new ThrowingHandler()));
        var provider = CreateProvider();
        provider.ApiKey = "";

        var result = await tester.TestAsync(provider);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorKind.InvalidConfiguration, result.ErrorKind);
    }

    // 2026-08-02: the Anthropic-specific test
    // (`TestAsync_UsesAnthropicAuthHeadersAndModelsEndpoint`) is
    // retired with the catalog prune. Anthropic is no longer a
    // ship target, and the connection tester's protocol branching
    // collapsed to OpenAI-compatible / Bearer auth. The
    // CapturingHandler class below was its only consumer and is
    // removed alongside the test. The replacement test
    // (`TestAsync_UsesBearerAuthAndModelsEndpoint`) pins the
    // OpenAI-compatible shape that the tester actually sends
    // now — the previous test was locking down the protocol that
    // got deleted.

    [Fact]
    public async Task TestAsync_UsesBearerAuthAndModelsEndpoint()
    {
        var handler = new BearerCapturingHandler();
        var tester = new ProviderConnectionTester(new HttpClient(handler));

        var result = await tester.TestAsync(new ConfiguredLlmProvider
        {
            TemplateId = "minimax",
            ProtocolId = "openai",
            Name = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ApiKey = "minimax-key",
            SelectedModelId = "MiniMax-M3"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.minimax.io/v1/models", handler.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("minimax-key", handler.AuthorizationParameter);
        // Old anthropic-only headers must not leak into the
        // OpenAI-compatible request — the test pin down here is
        // that we kept the OpenAI path clean when we deleted the
        // anthropic branch.
        Assert.False(handler.HasAnthropicVersion);
        Assert.False(handler.HasXApiKey);
    }

    private static ConfiguredLlmProvider CreateProvider()
    {
        // MiniMax is the only catalog ship target as of 2026-08-02;
        // the tester's openai protocol path is what the daily
        // driver exercises, so the helper builds a MiniMax-shaped
        // openai-protocol provider.
        return new ConfiguredLlmProvider
        {
            TemplateId = "minimax",
            ProtocolId = "openai",
            Name = "MiniMax",
            BaseUrl = "https://api.minimax.io/v1",
            ApiKey = "key",
            SelectedModelId = "MiniMax-M3"
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("should not call network");
        }
    }

    private sealed class BearerCapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public bool HasAnthropicVersion { get; private set; }
        public bool HasXApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var auth = request.Headers.Authorization;
            AuthorizationScheme = auth?.Scheme;
            AuthorizationParameter = auth?.Parameter;
            HasAnthropicVersion = request.Headers.Contains("anthropic-version");
            HasXApiKey = request.Headers.Contains("x-api-key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
