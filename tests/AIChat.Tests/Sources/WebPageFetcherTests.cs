using System.Net;
using System.Net.Http;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Sources;

namespace AIChat.Tests.Sources;

// The fetcher's hard guarantees (URL gate, content-
// type gate, 5MB cap, body-size streaming) all need
// a real HttpClient pipeline to exercise. We inject
// a HttpClient whose transport is a fake handler so
// the tests don't hit the network and don't depend
// on any external site staying online.
public class WebPageFetcherTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public Exception? Throw { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }
            return Task.FromResult(Respond?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static WebPageFetcher NewFetcher(FakeHandler handler) =>
        new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

    [Fact]
    public async Task FetchAsync_InvalidUrl_ReturnsNull()
    {
        var fetcher = NewFetcher(new FakeHandler());
        Assert.Null(await fetcher.FetchAsync("not a url"));
        Assert.Null(await fetcher.FetchAsync(""));
    }

    [Fact]
    public async Task FetchAsync_NonHttpScheme_ReturnsNull()
    {
        // The fetcher only handles http / https —
        // file:// / ftp:// / data: return null so a
        // curious user pasting weird input doesn't
        // accidentally trigger local file reads.
        var fetcher = NewFetcher(new FakeHandler());
        Assert.Null(await fetcher.FetchAsync("file:///etc/passwd"));
        Assert.Null(await fetcher.FetchAsync("javascript:alert(1)"));
    }

    [Fact]
    public async Task FetchAsync_NonSuccessStatus_ReturnsNull()
    {
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var fetcher = NewFetcher(handler);
        Assert.Null(await fetcher.FetchAsync("https://example.com/missing"));
    }

    [Fact]
    public async Task FetchAsync_NonHtmlContentType_ReturnsNull()
    {
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"key\":\"value\"}",
                    System.Text.Encoding.UTF8, "application/json"),
            },
        };
        var fetcher = NewFetcher(handler);
        Assert.Null(await fetcher.FetchAsync("https://example.com/data.json"));
    }

    [Fact]
    public async Task FetchAsync_NetworkError_ReturnsNull()
    {
        var handler = new FakeHandler
        {
            Throw = new HttpRequestException("DNS lookup failed"),
        };
        var fetcher = NewFetcher(handler);
        Assert.Null(await fetcher.FetchAsync("https://nonexistent.example/"));
    }

    [Fact]
    public async Task FetchAsync_HtmlPage_ExtractsTitleAndContent()
    {
        var html = """
            <html>
            <head><title>Test Article</title></head>
            <body>
              <h1>Welcome</h1>
              <p>This is the first paragraph with <em>emphasized</em> text.</p>
              <p>This is the second paragraph.</p>
            </body>
            </html>
            """;
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
            },
        };
        var fetcher = NewFetcher(handler);

        var result = await fetcher.FetchAsync("https://example.com/article");

        Assert.NotNull(result);
        Assert.Equal("Test Article", result.Title);
        Assert.Equal("https://example.com/article", result.Url);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("Welcome", result.Content);
        Assert.Contains("first paragraph", result.Content);
        Assert.Contains("emphasized", result.Content);
        Assert.Contains("second paragraph", result.Content);
    }

    [Fact]
    public async Task FetchAsync_MissingTitle_FallsBackToHost()
    {
        // Some pages have no <title> (or a title
        // outside <head>). The fetcher falls back to
        // the URL's host so the display name in the
        // Sources list is never empty.
        var html = "<html><body>no title here</body></html>";
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
            },
        };
        var fetcher = NewFetcher(handler);

        var result = await fetcher.FetchAsync("https://blog.example.com/post");

        Assert.NotNull(result);
        Assert.Equal("blog.example.com", result.Title);
    }

    [Fact]
    public async Task FetchAsync_NonAsciiTitle_DecodesCorrectly()
    {
        // The fetcher's charset detection path
        // routes through the response's declared
        // charset. UTF-8 with a non-ASCII title
        // covers the same code path; the GB18030
        // provider isn't registered by default
        // on .NET so we don't need to add a
        // System.Text.Encoding.CodePages
        // dependency for a single test.
        var title = "中文标题测试 — AIChat";
        var html = $"<html><head><title>{title}</title></head><body>x</body></html>";
        var handler = new FakeHandler
        {
            Respond = _ =>
            {
                var content = new StringContent(html, System.Text.Encoding.UTF8, "text/html");
                content.Headers.ContentType!.CharSet = "utf-8";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            },
        };
        var fetcher = NewFetcher(handler);

        var result = await fetcher.FetchAsync("https://example.cn/");

        Assert.NotNull(result);
        Assert.Equal(title, result.Title);
    }

    [Fact]
    public async Task FetchAsync_HugeBody_TruncatesAtCap()
    {
        // The fetcher caps the body at 5MB during
        // the stream read so a malicious server
        // can't blow up the app's memory. We test
        // the cap by sending a body that exceeds it
        // and verifying the result is still
        // returned (truncated) rather than throwing.
        // (The Reduced() maxLength cap is 200_000
        // chars; the body stream cap is 5MB. We
        // exceed only the Reduce cap, so the test
        // is fast and the result is well-formed.)
        var big = string.Concat(Enumerable.Repeat("word ", 100_000)); // ~500KB
        var html = $"<html><head><title>Big</title></head><body>{big}</body></html>";
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
            },
        };
        var fetcher = NewFetcher(handler);

        var result = await fetcher.FetchAsync("https://example.com/big");

        Assert.NotNull(result);
        Assert.Equal("Big", result.Title);
        // Reduce caps at 200_000 + ellipsis.
        Assert.True(result.Content.Length <= 200_001,
            $"Reduced length {result.Content.Length} should be <= 200_001");
    }
}
