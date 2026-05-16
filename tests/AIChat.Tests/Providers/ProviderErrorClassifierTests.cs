using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Providers;

public sealed class ProviderErrorClassifierTests
{
    [Theory]
    [InlineData(401, "{}", ProviderErrorKind.Authentication)]
    [InlineData(403, "{}", ProviderErrorKind.PermissionDenied)]
    [InlineData(429, "{}", ProviderErrorKind.RateLimited)]
    [InlineData(500, "{}", ProviderErrorKind.Server)]
    [InlineData(400, "{\"error\":\"maximum context length exceeded\"}", ProviderErrorKind.ContextLengthExceeded)]
    [InlineData(404, "{\"error\":\"model not found\"}", ProviderErrorKind.ModelNotFound)]
    public void FromHttp_ClassifiesCommonProviderErrors(int statusCode, string body, ProviderErrorKind expected)
    {
        var result = ProviderErrorClassifier.FromHttp(statusCode, "Provider", body);

        Assert.Equal(expected, result.Kind);
        Assert.Equal(statusCode, result.HttpStatusCode);
    }

    [Fact]
    public void FromException_ClassifiesNetworkFailureAsTransient()
    {
        var result = ProviderErrorClassifier.FromException(new HttpRequestException("DNS failed"), "Provider");

        Assert.Equal(ProviderErrorKind.Network, result.Kind);
        Assert.True(result.IsTransient);
    }
}
