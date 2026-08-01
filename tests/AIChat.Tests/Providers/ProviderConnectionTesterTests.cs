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

    [Fact]
    public async Task TestAsync_UsesAnthropicAuthHeadersAndModelsEndpoint()
    {
        var handler = new CapturingHandler();
        var tester = new ProviderConnectionTester(new HttpClient(handler));

        var result = await tester.TestAsync(new ConfiguredLlmProvider
        {
            TemplateId = "anthropic",
            ProtocolId = "anthropic",
            Name = "Anthropic",
            BaseUrl = "https://api.anthropic.com",
            ApiKey = "anthropic-key",
            SelectedModelId = "claude-opus-4-6"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.anthropic.com/v1/models", handler.RequestUri!.ToString());
        Assert.True(handler.HasAnthropicApiKey);
    }

    private static ConfiguredLlmProvider CreateProvider()
    {
        return new ConfiguredLlmProvider
        {
            TemplateId = "deepseek",
            ProtocolId = "openai",
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = "key",
            SelectedModelId = "deepseek-v4-pro"
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

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public bool HasAnthropicApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            HasAnthropicApiKey = request.Headers.TryGetValues("x-api-key", out var values) &&
                                 values.Contains("anthropic-key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
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
}
