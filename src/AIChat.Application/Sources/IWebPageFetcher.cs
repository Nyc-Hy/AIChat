using AIChat.Domain.Sources;

namespace AIChat.Application.Sources;

// Fetches a URL and reduces it to plain text the
// agent can reference. Lives in AIChat.Application so
// the fetch path is testable without Avalonia; the
// concrete HttpClient-based implementation lives in
// the App layer (which is fine — HttpClient is a
// standard .NET API, not Avalonia-specific).
//
// Wave 7 (parity plan §7 Wave 7) first slice. The
// contract: caller passes a URL string, gets back a
// Source-shaped result (Title + Content + Url) or null
// when the URL is unreachable / non-HTML / parse-
// failed. The caller (the AddWebSearchSource_OnClick
// handler) decides what to do with null — typically
// surface a user-visible error.
public interface IWebPageFetcher
{
    Task<WebFetchResult?> FetchAsync(string url, CancellationToken cancellationToken = default);
}

// Plain-text extraction of an HTML page. Lives next
// to the fetcher because every implementation is going
// to want this — pulling it into a shared helper
// avoids copy-paste between the desktop host and any
// future headless / CLI consumer.
public sealed class WebFetchResult
{
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public int StatusCode { get; init; }
}
