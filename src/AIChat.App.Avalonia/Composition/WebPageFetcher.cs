using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AIChat.Application.Sources;

namespace AIChat.App.Avalonia.Composition;

// HttpClient-backed IWebPageFetcher. The desktop host
// owns the HttpClient lifetime (singleton via DI) so
// the agent loop + the cron engine + the Sources
// panel share a single connection pool.
//
// HTML is the only content type we accept — JSON /
// PDF / image URLs return null and the caller
// surfaces "unsupported content type" to the user. A
// follow-up slice that wants PDF support can add an
// ITikaExtractor / pdf-to-text sidecar; for now PDFs
// return null and the user pastes the text manually
// (the clipboard-source path already covers that).
public sealed class WebPageFetcher : IWebPageFetcher
{
    private const int MaxResponseBytes = 5 * 1024 * 1024; // 5 MB cap
    private const int FetchTimeoutSeconds = 15;

    private readonly HttpClient _client;

    public WebPageFetcher(HttpClient? client = null)
    {
        _client = client ?? BuildDefaultClient();
    }

    private static HttpClient BuildDefaultClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(FetchTimeoutSeconds),
        };
        // A real User-Agent stops some sites (notably
        // Wikipedia, GitHub, dev.to) from returning a
        // 403 to a "User-Agent: <blank>" client. The
        // string matches the form Firefox / Safari send;
        // most CDNs whitelist it.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        return client;
    }

    public async Task<WebFetchResult?> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // DNS / connect / TLS / etc. The user
            // sees "无法访问 <url>" via the
            // caller's error path.
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's Timeout throws TaskCanceledException
            // when the configured budget elapses; the
            // caller's cancellation token is the
            // "user gave up" path which is a different
            // exception type (OperationCanceledException
            // is a subclass of TaskCanceledException, so
            // the !cancellationToken.IsCancellationRequested
            // guard distinguishes the two).
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Content-Type gate. The Accept header
            // already biases the response toward
            // HTML, but a misconfigured server can
            // return anything; we only run the
            // reducer on HTML.
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!IsHtmlContentType(contentType))
            {
                return null;
            }

            // Stream the body with a hard byte cap so a
            // malicious server can't blow up the app's
            // memory by serving a 10GB "page". The
            // cap is checked during the read, not via
            // Content-Length (which the server can lie
            // about).
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var buffer = new byte[MaxResponseBytes];
            var totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead >= MaxResponseBytes)
                {
                    break;
                }
            }

            // Best-effort charset detection. The
            // response's Content-Type charset (when
            // present) takes precedence; otherwise we
            // fall back to the meta-charset sniff on
            // the first 1KB so pages declaring
            // <meta charset="utf-8"> decode correctly
            // even when the server sent the wrong
            // Content-Type.
            var encoding = response.Content.Headers.ContentType?.CharSet is { } declared
                ? TryEncoding(declared)
                : null;
            var raw = encoding?.GetString(buffer, 0, totalRead)
                ?? SniffEncoding(buffer, totalRead).GetString(buffer, 0, totalRead);

            var title = HtmlToText.ExtractTitle(raw);
            var content = HtmlToText.Reduce(raw);

            return new WebFetchResult
            {
                Url = uri.ToString(),
                Title = string.IsNullOrEmpty(title) ? uri.Host : title,
                Content = content,
                StatusCode = (int)response.StatusCode,
            };
        }
    }

    private static bool IsHtmlContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }
        // Strip parameters (e.g. "text/html; charset=utf-8").
        var semi = contentType.IndexOf(';');
        var bare = semi >= 0 ? contentType[..semi] : contentType;
        bare = bare.Trim();
        return string.Equals(bare, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bare, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding? TryEncoding(string name)
    {
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return null;
        }
    }

    // Look for <meta charset="..."> / <meta http-equiv="Content-Type"...>
    // in the first 1KB. Falls back to UTF-8 when no
    // declaration is found.
    private static Encoding SniffEncoding(byte[] buffer, int length)
    {
        var sniffLen = Math.Min(length, 1024);
        var prefix = System.Text.Encoding.ASCII.GetString(buffer, 0, sniffLen);
        var match = System.Text.RegularExpressions.Regex.Match(
            prefix,
            @"charset\s*=\s*[""']?([\w\-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var enc = TryEncoding(match.Groups[1].Value);
            if (enc is not null)
            {
                return enc;
            }
        }
        return System.Text.Encoding.UTF8;
    }
}
