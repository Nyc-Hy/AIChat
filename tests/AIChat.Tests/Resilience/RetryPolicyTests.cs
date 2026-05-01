using AIChat.Application.Llm.Resilience;

namespace AIChat.Tests.Resilience;

public sealed class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_SucceedsOnFirstAttempt()
    {
        var policy = new RetryPolicy(maxRetries: 3);
        var callCount = 0;

        var result = await policy.ExecuteAsync(ct =>
        {
            callCount++;
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnTransientException()
    {
        var policy = new RetryPolicy(maxRetries: 3, baseDelay: TimeSpan.FromMilliseconds(1));
        var callCount = 0;

        var result = await policy.ExecuteAsync(ct =>
        {
            callCount++;
            if (callCount < 3)
                throw new HttpRequestException("connection refused");
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsAfterMaxRetries()
    {
        var policy = new RetryPolicy(maxRetries: 2, baseDelay: TimeSpan.FromMilliseconds(1));
        var callCount = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            policy.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new HttpRequestException("always fails");
            }));

        Assert.Equal(3, callCount); // 1 initial + 2 retries
    }

    [Fact]
    public void IsTransientHttpError_429IsRetryable()
    {
        Assert.True(RetryPolicy.IsTransientHttpError(429));
    }

    [Fact]
    public void IsTransientHttpError_500IsRetryable()
    {
        Assert.True(RetryPolicy.IsTransientHttpError(500));
    }

    [Fact]
    public void IsTransientHttpError_400IsNotRetryable()
    {
        Assert.False(RetryPolicy.IsTransientHttpError(400));
    }

    [Fact]
    public void IsTransientHttpError_401IsNotRetryable()
    {
        Assert.False(RetryPolicy.IsTransientHttpError(401));
    }

    [Fact]
    public void GetDelay_IncreasesExponentially()
    {
        var policy = new RetryPolicy(maxRetries: 3);

        var delay0 = policy.GetDelay(0);
        var delay1 = policy.GetDelay(1);
        var delay2 = policy.GetDelay(2);

        // Base delays: 1s, 2s, 4s (plus jitter)
        Assert.True(delay0.TotalMilliseconds >= 1000);
        Assert.True(delay1.TotalMilliseconds >= 2000);
        Assert.True(delay2.TotalMilliseconds >= 4000);
    }
}
