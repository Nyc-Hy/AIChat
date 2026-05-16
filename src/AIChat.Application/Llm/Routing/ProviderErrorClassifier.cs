using System.Net.Http;

namespace AIChat.Application.Llm.Routing;

public static class ProviderErrorClassifier
{
    public static ProviderErrorInfo FromHttp(int statusCode, string providerName, string body = "")
    {
        var normalizedBody = body.ToLowerInvariant();
        var kind = statusCode switch
        {
            400 when ContainsAny(normalizedBody, "context_length", "maximum context", "too many tokens", "token limit") =>
                ProviderErrorKind.ContextLengthExceeded,
            400 => ProviderErrorKind.InvalidRequest,
            401 => ProviderErrorKind.Authentication,
            403 => ProviderErrorKind.PermissionDenied,
            404 when ContainsAny(normalizedBody, "model", "not found") => ProviderErrorKind.ModelNotFound,
            404 => ProviderErrorKind.InvalidRequest,
            408 => ProviderErrorKind.Timeout,
            422 when ContainsAny(normalizedBody, "context", "token") => ProviderErrorKind.ContextLengthExceeded,
            422 => ProviderErrorKind.InvalidRequest,
            429 => ProviderErrorKind.RateLimited,
            >= 500 and <= 599 => ProviderErrorKind.Server,
            _ => ProviderErrorKind.Unknown
        };

        return Create(kind, providerName, statusCode, body);
    }

    public static ProviderErrorInfo FromException(Exception exception, string providerName)
    {
        var kind = exception switch
        {
            TaskCanceledException => ProviderErrorKind.Timeout,
            OperationCanceledException => ProviderErrorKind.Timeout,
            HttpRequestException => ProviderErrorKind.Network,
            _ => ProviderErrorKind.Unknown
        };
        return Create(kind, providerName, null, exception.Message);
    }

    public static ProviderErrorInfo FromDelta(int? statusCode, string providerName, string bodyOrContent)
    {
        return statusCode is > 0
            ? FromHttp(statusCode.Value, providerName, bodyOrContent)
            : Create(ProviderErrorKind.Unknown, providerName, null, bodyOrContent);
    }

    private static ProviderErrorInfo Create(
        ProviderErrorKind kind,
        string providerName,
        int? statusCode,
        string detail)
    {
        var title = kind switch
        {
            ProviderErrorKind.Authentication => "API Key 无效或缺失",
            ProviderErrorKind.PermissionDenied => "账号没有访问权限",
            ProviderErrorKind.RateLimited => "请求频率或额度受限",
            ProviderErrorKind.ModelNotFound => "模型不存在或不可用",
            ProviderErrorKind.ContextLengthExceeded => "上下文超过模型限制",
            ProviderErrorKind.InvalidRequest => "请求参数无效",
            ProviderErrorKind.Network => "网络连接失败",
            ProviderErrorKind.Timeout => "请求超时",
            ProviderErrorKind.Server => "模型服务端错误",
            ProviderErrorKind.InvalidConfiguration => "模型配置无效",
            _ => "模型请求失败"
        };
        var transient = kind is ProviderErrorKind.RateLimited or ProviderErrorKind.Timeout or ProviderErrorKind.Network or ProviderErrorKind.Server;
        var statusText = statusCode is null ? "" : $"HTTP {statusCode}: ";
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{providerName} 返回错误：{title}。"
            : $"{providerName} 返回错误：{statusText}{title}。\n\n{detail.Trim()}";
        return new ProviderErrorInfo(kind, title, message, statusCode, transient);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
