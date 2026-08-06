using System.Net;
using System.Text;

namespace AIChat.Application.Sites;

// 2026-08-03: in-process static file server for Sites preview.
// Replaces the original `python3 -m http.server` approach so the
// preview works on Windows (where python3 is not in PATH) and on
// macOS / Linux installations that ship without Python at all. The
// server is intentionally minimal: GET only, no range requests, no
// directory listing when no index.html is present, no caching
// headers. Sites are static; the user does not need HTTP/1.1
// features beyond serving the file bytes.
//
// Lifecycle: StartAsync begins listening and returns the bound
// port; the caller passes the port to the site registry / URL
// hint. The server is bound to a CancellationToken so a single
// Stop call (or app shutdown) tears down the listener without
// leaking the worker thread. Multiple sites can be live at the
// same time, each with its own EmbeddedStaticFileServer instance
// on a different port.
//
// Windows note: System.Net.HttpListener on Windows requires the
// URL to be ACL'd for the current user via
// `netsh http add urlacl url=http://+:<port>/ user=Everyone`.
// Self-hosted loopback URLs (`http://localhost:<port>/`) are
// reserved by default for the owner so this is rarely a problem
// in practice; if a deployment hits the ACL gate the
// PlatformHttpListenerFactory surfaces the Win32Exception so the
// UI can show a one-line fix.
public sealed class EmbeddedStaticFileServer
{
    private readonly string _rootPath;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private int _port;

    public EmbeddedStaticFileServer(int port, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must be a non-empty directory.", nameof(rootPath));
        }
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root path does not exist: {rootPath}");
        }

        _rootPath = Path.GetFullPath(rootPath);
        _port = port;
        _listener = new HttpListener();
        // Loopback prefix. The "+" wildcard is intentionally
        // avoided because it requires admin / URL ACL on Windows.
        // Loopback is what the user actually needs: a local
        // preview opened from a browser on the same machine.
        var prefix = $"http://localhost:{port}/";
        _listener.Prefixes.Add(prefix);
    }

    public int Port => _port;
    public string RootPath => _rootPath;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"无法在 localhost:{_port} 启动静态文件服务。端口被占用，或 Windows 需要 'netsh http add urlacl url=http://localhost:{_port}/ user={Environment.UserName}': {ex.Message}",
                ex);
        }

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), cancellationToken);
    }

    public void Stop()
    {
        try
        {
            _cts.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            _listener.Close();
        }
        catch
        {
            // Stop is best-effort; the caller's StopAsync on the
            // supervisor already returned successfully if the
            // process is gone.
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                // Listener was stopped; this is the normal
                // shutdown path.
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                // Swallow and keep the loop alive so a single
                // bad request does not kill the server.
                continue;
            }

            try
            {
                HandleRequest(context);
            }
            catch
            {
                TryCloseWithStatus(context, 500);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            TryCloseWithStatus(context, 405);
            return;
        }

        var urlPath = context.Request.Url?.AbsolutePath ?? "/";
        var relative = Uri.UnescapeDataString(urlPath).TrimStart('/');
        if (string.IsNullOrEmpty(relative))
        {
            relative = "index.html";
        }
        // Defence in depth: even though we sanitise the path,
        // resolve it and confirm it stays inside _rootPath.
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(candidate, _rootPath, StringComparison.Ordinal))
        {
            TryCloseWithStatus(context, 403);
            return;
        }

        if (Directory.Exists(candidate))
        {
            var index = Path.Combine(candidate, "index.html");
            if (File.Exists(index))
            {
                candidate = index;
            }
            else
            {
                // No directory listing — the user is editing a
                // static site, not exposing a file share.
                TryCloseWithStatus(context, 404);
                return;
            }
        }

        if (!File.Exists(candidate))
        {
            TryCloseWithStatus(context, 404);
            return;
        }

        ServeFile(context, candidate);
    }

    private void ServeFile(HttpListenerContext context, string path)
    {
        var extension = Path.GetExtension(path);
        context.Response.ContentType = ContentTypeFor(extension);
        context.Response.Headers["Cache-Control"] = "no-store";
        if (string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentLength64 = new FileInfo(path).Length;
            context.Response.Close();
            return;
        }
        try
        {
            var bytes = File.ReadAllBytes(path);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            TryCloseWithStatus(context, 500);
        }
    }

    private static void TryCloseWithStatus(HttpListenerContext context, int status)
    {
        try
        {
            context.Response.StatusCode = status;
            var body = Encoding.UTF8.GetBytes($"{status}");
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // Connection may already be closed by the client.
        }
    }

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".txt" => "text/plain; charset=utf-8",
        ".md" => "text/markdown; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".pdf" => "application/pdf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}
