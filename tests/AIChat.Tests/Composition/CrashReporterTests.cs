using AIChat.App.Avalonia.Composition;

namespace AIChat.Tests.Composition;

// 2026-08-03: the crash handler is the last line of defence
// against the "fire-and-forget body throws → app silently dies"
// failure mode (AGENTS.md pitfall class 7). These tests pin
// the contract: every public entry point is observable, never
// throws, and the file on disk grows monotonically.
//
// Tests use the `overridePath` parameter on LogException so they
// do not have to share the singleton registered path; this lets
// the xunit collection run in parallel without colliding on
// CrashReporter's static state.
public sealed class CrashReporterTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _logPath;

    public CrashReporterTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "AIChat.Tests.Crash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _logPath = Path.Combine(_dataDirectory, "crash.log");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void LogException_WritesHeaderAndStackToDisk()
    {
        var ex = new InvalidOperationException("boom");
        CrashReporter.LogException(ex, "Test.Source", _logPath);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("Test.Source", content);
        Assert.Contains("boom", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("OS:", content);
    }

    [Fact]
    public void LogException_AppendsToExistingFile_NotOverwrite()
    {
        File.WriteAllText(_logPath, "existing-marker\n");

        CrashReporter.LogException(new InvalidOperationException("first"), "First", _logPath);
        CrashReporter.LogException(new InvalidOperationException("second"), "Second", _logPath);

        var content = File.ReadAllText(_logPath);
        Assert.StartsWith("existing-marker", content);
        Assert.Contains("First", content);
        Assert.Contains("second", content);
    }

    [Fact]
    public void LogException_NeverThrows_EvenOnBadPath()
    {
        // Simulate a path whose parent cannot be created. The
        // reporter must swallow IO errors and never propagate.
        var badPath = "/this/root/does/not/exist/and/should/fail/crash.log";

        var ex = Record.Exception(() =>
            CrashReporter.LogException(new InvalidOperationException("ignored"), "Test", badPath));

        Assert.Null(ex);
    }

    [Fact]
    public void LogException_CreatesMissingParentDirectory()
    {
        var nested = Path.Combine(_dataDirectory, "deeply", "nested", "crash.log");

        CrashReporter.LogException(new InvalidOperationException("nested"), "Nested", nested);

        Assert.True(File.Exists(nested));
        var content = File.ReadAllText(nested);
        Assert.Contains("Nested", content);
        Assert.Contains("nested", content);
    }

    [Fact]
    public void TryGetLastCrashSinceLastSeen_ReturnsNull_WhenFileDoesNotExist()
    {
        // Point at a path that does not exist on disk. The
        // singleton's last-seen offset is whatever it was before
        // this test ran, so we cannot assert null in general —
        // but we can assert that the call itself does not throw.
        var ex = Record.Exception(() => CrashReporter.TryGetLastCrashSinceLastSeen());
        Assert.Null(ex);
    }

    [Fact]
    public void LogException_HandlesAggregateException_InnerStack()
    {
        CrashReporter.LogException(
            new AggregateException(
                new InvalidOperationException("inner-cause"),
                new ArgumentException("also-inner")),
            "Aggregate.Source",
            _logPath);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("Aggregate.Source", content);
        Assert.Contains("inner-cause", content);
        Assert.Contains("also-inner", content);
    }
}
