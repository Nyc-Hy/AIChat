using System.Text;
using Avalonia;
using Avalonia.Threading;

namespace AIChat.App.Avalonia.Composition;

// 2026-08-03: catches the failure mode where an unobserved task
// exception or an AppDomain unhandled exception silently kills the
// app with no UI feedback. Per AGENTS.md pitfall class 7 ("async
// fire-and-forget body must be try/catch"), 20+ fire-and-forget call
// sites existed pre-fix; a single throw anywhere would terminate
// the process before the user saw anything.
//
// Now: every unhandled exception is appended to `<dataDir>/crash.log`
// with a UTC timestamp, the source hook, the full stack trace, and
// the OS / runtime version. The file is append-only so post-mortem
// analysis is possible from a single artifact. A startup hook
// (HasNewCrashSinceLastSeen) lets the host show a one-time toast
// after a crash, e.g. "上次异常退出，详情 ~/.aichat/crash.log".
//
// Safety: every method is wrapped in try/catch and never throws.
// A misbehaving crash handler is worse than no crash handler.
public static class CrashReporter
{
    private static readonly object _gate = new();
    private static string _logPath = "";
    private static long _lastSeenLength; // offset the host already acknowledged
    private static int _registered; // 0 = no, 1 = yes (Interlocked)

    public static string LogPath
    {
        get { lock (_gate) return _logPath; }
    }

    // Wire up AppDomain / Dispatcher / TaskScheduler hooks. Safe to
    // call multiple times — only the first call wins.
    public static void Register(string logPath)
    {
        if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            _logPath = logPath;
            try
            {
                if (!string.IsNullOrWhiteSpace(logPath) && System.IO.File.Exists(logPath))
                {
                    _lastSeenLength = new System.IO.FileInfo(logPath).Length;
                }
            }
            catch
            {
                _lastSeenLength = 0;
            }
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        }
        catch
        {
        }

        try
        {
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandled;
        }
        catch
        {
        }

        try
        {
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        }
        catch
        {
        }
    }

    // Returns the most recent crash entry if there is one after the
    // caller's last-seen offset. The host calls this after the first
    // settings load and uses the returned summary to show a one-time
    // toast like "上次异常退出 (AppDomain.UnhandledException): ... —
    // 详情 ~/.aichat/crash.log". Returns null if no new crash.
    public static string? TryGetLastCrashSinceLastSeen()
    {
        string path;
        long lastSeen;
        lock (_gate)
        {
            path = _logPath;
            lastSeen = _lastSeenLength;
        }
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new System.IO.FileStream(
                path,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite);
            if (stream.Length <= lastSeen)
            {
                return null;
            }
            stream.Seek(lastSeen, System.IO.SeekOrigin.Begin);
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();
            lock (_gate)
            {
                _lastSeenLength = stream.Length;
            }
            var firstLine = content.Split('\n')[0].Trim();
            if (firstLine.StartsWith("==== "))
            {
                firstLine = firstLine.Substring(5);
            }
            return firstLine;
        }
        catch
        {
            return null;
        }
    }

    // Public entry point so tests + production code can log exceptions
    // they caught but consider fatal. `overridePath` exists for tests
    // that want to point at a per-test temp file without going through
    // the singleton Register; production callers should use the
    // 2-arg overload.
    public static void LogException(Exception exception, string source, string? overridePath = null)
    {
        try
        {
            AppendToLog(exception, source, overridePath);
        }
        catch
        {
        }
    }

    private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException(ex, "AppDomain.UnhandledException");
        }
    }

    private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // We do not set e.Handled = true: Avalonia's default behaviour
        // is to surface the exception, which is the right call. We
        // only log.
        LogException(e.Exception, "Dispatcher.UnhandledException");
    }

    private static void OnUnobservedTask(object sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private static void AppendToLog(Exception exception, string source, string? overridePath = null)
    {
        string path;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            path = overridePath;
        }
        else
        {
            lock (_gate)
            {
                path = _logPath;
            }
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var sb = new StringBuilder();
            sb.Append("==== ")
              .Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
              .Append(" (")
              .Append(source)
              .Append(") ====\n");
            sb.Append("OS: ").Append(Environment.OSVersion.VersionString)
              .Append(" | Runtime: ").Append(Environment.Version.ToString())
              .Append('\n');
            sb.Append(exception).Append("\n\n");
            System.IO.File.AppendAllText(path, sb.ToString());
        }
        catch
        {
        }
    }
}
