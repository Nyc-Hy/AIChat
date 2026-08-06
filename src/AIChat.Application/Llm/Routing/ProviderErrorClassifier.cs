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
        // 2026-08-04: actionable hint per error kind. The
        // previous error surface was a wall of "HTTP 401
        // invalid api key" with no next step — the
        // daily driver had to read the response body,
        // figure out "is the key dead or is the model
        // tier wrong", and dig through docs. The hints
        // here route the user to the right next click
        // in the Settings modal (switch model, get a
        // different key, edit baseUrl) or out to the
        // platform (billing dashboard). Specific numbers
        // (e.g. "M3 lives on Token Plan, not Coding
        // Plan") come from the 2026-08 catalog research
        // — see the commit message on the catalog
        // change for the full MiniMax pricing reference.
        var hint = kind switch
        {
            // 401 covers both "key is dead / revoked" and
            // "key is on the wrong tier for the model
            // you picked". 2026-08-05: MiniMax unified the
            // Coding Plan + Token Plan billing surfaces,
            // so sk-cp-… keys now authenticate against M3
            // too. The old "switch to M2.7" instruction
            // is misleading now (it'd route the user
            // AWAY from a model their key pays for). The
            // remaining 401 causes are: dead/revoked key
            // (regenerate in the console) or a typo
            // (paste error). Both fixes are out-of-app.
            ProviderErrorKind.Authentication =>
                "检查 API Key 是否过期或被撤销（重新生成见 MiniMax 控制台「API Keys」页），或确认粘贴时没漏字符。",
            // 403 is "key valid, account blocked from this
            // resource" — billing / region / org
            // permission issue. Out of app.
            ProviderErrorKind.PermissionDenied =>
                "账号可能欠费、组织被停用、或当前区域不支持该模型。去 MiniMax 控制台账单/区域设置页确认。",
            // 429 = rate limit OR quota. For Token Plan
            // subscribers, the most common cause is
            // monthly quota exhaustion, not a per-second
            // rate limit. The hint points at the
            // dashboard's usage view.
            ProviderErrorKind.RateLimited =>
                "可能是按量 RPM/TPM 限流，也可能是 Token Plan 月度额度用完。等几十秒重试；如果还失败，去 MiniMax 控制台「用量」页看余额。",
            // 404 is the second most common 2026-08
            // trap: the model id was typed free-form
            // (ResolveModel's "non-empty user-typed id"
            // path) and the platform doesn't recognize
            // it. M2 / M2.1 also hit this when the base
            // url is wrong (we saw 401 on .io, but a
            // correctly-keyed .io sometimes returns 404
            // for older model ids).
            ProviderErrorKind.ModelNotFound =>
                "当前 model id 在 MiniMax 上不存在或被下线了。打开 Settings 把模型换成 M3 / M3-highspeed 之一试试。",
            // 422 / 400 + context keywords = prompt too
            // long. M2.7 caps at 200K, M3 at 1M. The
            // hint routes the user to the model dropdown
            // (switch to M3 to get the 1M budget) or the
            // /new command (start a new conversation to
            // clear context).
            ProviderErrorKind.ContextLengthExceeded =>
                "对话历史 + 输入超过当前模型上下文窗口。/new 开新对话，或在 Settings 把模型换成 M3 (1M 上下文)。",
            // 400 with no context keyword is the
            // 'developer' role case or a malformed
            // tool call. The previous shape just said
            // "InvalidRequest" with no hint, which left
            // the user reading JSON payloads to figure
            // out which role is wrong.
            ProviderErrorKind.InvalidRequest =>
                "通常是请求体里用了 'developer' role（MiniMax 不支持）或者工具调用格式不对。看一眼详细错误里的 role 字段，把 developer 改成 system。",
            // Network errors are almost always a
            // firewall / baseUrl typo. The default
            // .io→.chat migration covers most of these
            // (added in 2a247af) but a self-hosted
            // proxy at a custom host could still hit
            // it.
            ProviderErrorKind.Network =>
                "网络层失败。检查 baseUrl 拼写（默认应该是 https://api.minimax.chat/v1），或公司代理/VPN 是否拦截了出站 HTTPS。",
            // Timeout: 20s client-side default. A 1M
            // context prefill can blow past 20s. The
            // hint points at /clear or the model
            // dropdown, not at the user trying to
            // "wait it out".
            ProviderErrorKind.Timeout =>
                "20 秒没收到响应。长 prompt 配 1M 上下文 prefill 容易超时。/clear 缩短上下文，或换 M3-highspeed 优先调度档。",
            // 5xx: transient. The user should just
            // retry. No actionable hint — the previous
            // shape didn't have one either, and "wait
            // and retry" is a fine default.
            ProviderErrorKind.Server => "",
            // The "settings file is broken" case.
            // The user can usually see this in the
            // Settings modal before the request
            // fires; the hint routes them back to the
            // same place.
            ProviderErrorKind.InvalidConfiguration =>
                "去 Settings 检查 baseUrl / model / key 三项是否填全了。",
            // Catch-all. Empty hint so the Settings
            // surface doesn't show a "what to do
            // next" line that we don't actually have
            // a good answer for.
            _ => ""
        };
        var statusText = statusCode is null ? "" : $"HTTP {statusCode}: ";
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{providerName} 返回错误：{title}。"
            : $"{providerName} 返回错误：{statusText}{title}。\n\n{detail.Trim()}";
        return new ProviderErrorInfo(kind, title, message, statusCode, transient, hint);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
