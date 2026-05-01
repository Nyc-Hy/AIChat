namespace AIChat.Application.Llm.Resilience;

/// <summary>
/// Exponential backoff retry policy for transient LLM provider errors.
/// </summary>
public sealed class RetryPolicy
{
    public int MaxRetries { get; }
    private readonly TimeSpan _baseDelay;
    private static readonly Random JitterRandom = new();

    public RetryPolicy(int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        MaxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// HTTP status codes that are safe to retry.
    /// </summary>
    public static bool IsTransientHttpError(int statusCode)
    {
        return statusCode is 429 or 500 or 502 or 503 or 504;
    }

    /// <summary>
    /// Execute an async operation with retry on transient failures.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool>? isTransientResult = null,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await operation(cancellationToken);

                if (isTransientResult is null || !isTransientResult(result))
                {
                    return result;
                }

                // Result indicates a transient error — retry
                if (attempt < MaxRetries)
                {
                    await Task.Delay(GetDelay(attempt), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User cancelled — don't retry
                throw;
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                lastException = ex;
                if (attempt < MaxRetries)
                {
                    await Task.Delay(GetDelay(attempt), cancellationToken);
                }
            }
        }

        throw lastException ?? new InvalidOperationException("Retry policy exhausted without exception");
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException or IOException or TimeoutException or TaskCanceledException;
    }

    public TimeSpan GetDelay(int attempt)
    {
        // Exponential backoff: 2^attempt seconds + random jitter (0-500ms)
        var baseMs = (int)Math.Pow(2, attempt) * 1000;
        var jitter = JitterRandom.Next(0, 500);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }
}
