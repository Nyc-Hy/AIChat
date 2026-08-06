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

    // 2026-08-04: every error kind the user can
    // actually hit on MiniMax gets an actionable
    // hint. The previous shape returned a wall of
    // "HTTP 401 invalid api key" with no next step;
    // the new shape routes the user to the right
    // click (Settings → switch model, or out to
    // the platform's billing dashboard). The
    // assertion here is that the hint is non-empty
    // AND mentions the specific fix (not a generic
    // "try again" loop) so a future regression to
    // empty hints breaks here rather than as a
    // daily-driver "the toast said nothing useful"
    // support ticket.
    [Theory]
    [InlineData(401, "{\"error\":\"invalid api key\"}", ProviderErrorKind.Authentication, "API Key")]
    [InlineData(403, "{}", ProviderErrorKind.PermissionDenied, "账单")]
    [InlineData(429, "{}", ProviderErrorKind.RateLimited, "用量")]
    [InlineData(404, "{\"error\":\"model not found\"}", ProviderErrorKind.ModelNotFound, "M3")]
    [InlineData(400, "{\"error\":\"maximum context length exceeded\"}", ProviderErrorKind.ContextLengthExceeded, "1M 上下文")]
    [InlineData(400, "{\"error\":\"developer role not supported\"}", ProviderErrorKind.InvalidRequest, "system")]
    public void FromHttp_HasActionableHintForEveryCommonKind(
        int statusCode, string body, ProviderErrorKind expectedKind, string hintKeyword)
    {
        // The hint keyword is a substring that MUST
        // appear in the remediation message. Picking
        // keywords the user has actually seen (the
        // 2026-08 user testing surfaces all of them)
        // means a future hint rewrite that loses the
        // actionable specificity ("the key is wrong,
        // pick a different model" → "please try again
        // later") breaks the test.
        var result = ProviderErrorClassifier.FromHttp(statusCode, "MiniMax", body);

        Assert.Equal(expectedKind, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.RemediationHint),
            $"{expectedKind} must surface an actionable hint — daily drivers were getting 401s with no next step before this field existed");
        Assert.Contains(hintKeyword, result.RemediationHint,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromHttp_ServerError_HasNoHint_ButStillCarriesMessage()
    {
        // 5xx is transient — the right next step is
        // "wait and retry", which is a one-line answer
        // that doesn't need a remediation hint. The
        // surface should still carry the title +
        // message so the toast isn't empty.
        var result = ProviderErrorClassifier.FromHttp(503, "MiniMax", "{\"error\":\"upstream timeout\"}");

        Assert.Equal(ProviderErrorKind.Server, result.Kind);
        Assert.True(result.IsTransient);
        Assert.True(string.IsNullOrEmpty(result.RemediationHint));
        Assert.Contains("upstream timeout", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
