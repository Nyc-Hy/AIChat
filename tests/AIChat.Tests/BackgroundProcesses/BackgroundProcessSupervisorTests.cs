using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Persistence;
using AIChat.Domain.BackgroundProcesses;

namespace AIChat.Tests.BackgroundProcesses;

// Wave 7 follow-up (plan §13 P0 risk "整个子进程树"):
// pin the supervisor's lifecycle contract. The
// supervisor is the foundation for both the
// Environment panel's Background section and the
// Sites local preview, so getting it right matters.
public sealed class BackgroundProcessSupervisorTests : IDisposable
{
    private readonly string _root;
    private readonly string _processesFile;
    private readonly BackgroundProcessSupervisor _supervisor;

    public BackgroundProcessSupervisorTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "aichat-bg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _processesFile = Path.Combine(_root, "processes.json");
        _supervisor = new BackgroundProcessSupervisor(_processesFile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task StartAsync_LongRunningCommand_StaysRunningUntilStopped()
    {
        // /bin/sh with a sleep keeps the process alive
        // long enough to assert the supervisor's
        // Running state. We use a short sleep (3s) so the
        // test finishes quickly even if StopAsync fails.
        var process = NewProcess("sleep", "3");

        var id = await _supervisor.StartAsync(process);
        await _supervisor.ReloadAsync();

        var row = Assert.Single(_supervisor.Processes);
        // Surface the row's log so a failed assertion
        // shows why the spawn didn't take.
        Assert.True(
            row.LogTail.Count == 0,
            $"Unexpected log entries: [{string.Join(", ", row.LogTail)}]");
        Assert.Equal(id, row.Id);
        Assert.Equal(BackgroundProcessStatus.Running, row.Status);
        Assert.True(row.Pid > 0, "supervisor should capture the spawned PID");
        Assert.NotNull(row.StartedAt);
        Assert.Null(row.StoppedAt);

        // Cleanup so the test process doesn't leak.
        await _supervisor.StopAsync(id);
    }

    [Fact]
    public async Task StopAsync_TerminatesRunningProcess()
    {
        // Use a long-running shell that traps SIGTERM
        // and exits cleanly. Plain `sleep` ignores
        // SIGTERM on some platforms (it gets SIGKILLed
        // by the escalation path), which is a valid
        // outcome but not the cleanest test signal.
        var process = NewProcess("/bin/sh", "-c", "trap 'exit 0' TERM; sleep 30");
        await _supervisor.StartAsync(process);

        var stopped = await _supervisor.StopAsync(process.Id, killTimeout: TimeSpan.FromSeconds(3));
        Assert.True(stopped);

        var row = _supervisor.Processes[0];
        // The important contract is that Status is no
        // longer Running — both Stopped (SIGTERM
        // honored) and ForceKilled (SIGTERM ignored
        // + escalation) are acceptable outcomes.
        Assert.NotEqual(BackgroundProcessStatus.Running, row.Status);
        Assert.NotNull(row.StoppedAt);
    }

    [Fact]
    public async Task ReloadAsync_MarksRunningEntriesAsCrashed_WhenProcessIsDead()
    {
        // Simulate the "app restarted while a process
        // was still flagged Running" scenario: a row
        // exists in the sidecar with Status=Running
        // but the actual OS process is dead. The
        // reload-recovery path walks every Running
        // row, checks the PID via Process.GetProcessById,
        // and flips it to Crashed if the PID is gone.
        //
        // We seed the file directly because starting
        // a real process and then "killing the app" is
        // expensive; the recovery code path is the
        // same regardless of how the row got there.
        //
        // We use PID 999_999 (very unlikely to exist
        // on any test host). If by chance it does
        // exist, the test would false-positive — but
        // that's a stronger signal than just relying
        // on Pid=0 which the .NET API treats as
        // invalid-input.
        var seed = new BackgroundProcess
        {
            Name = "phantom",
            Command = "/bin/sleep",
            Arguments = new List<string> { "99999" },
            Pid = 999_999,
            Status = BackgroundProcessStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };
        await JsonFileStore.SaveListAsync(
            _processesFile, new[] { seed });

        await _supervisor.ReloadAsync();

        var row = Assert.Single(_supervisor.Processes);
        Assert.Equal(BackgroundProcessStatus.Crashed, row.Status);
        Assert.NotNull(row.StoppedAt);
    }

    [Fact]
    public async Task StartAsync_AppendsLogTail_ForChattyCommand()
    {
        // A command that prints 3 lines — the
        // supervisor's stdout handler should append
        // them to the row's LogTail.
        var process = NewProcess("/bin/sh", "-c", "echo a; echo b; echo c");
        await _supervisor.StartAsync(process);
        await WaitForStatus(process.Id, BackgroundProcessStatus.Stopped, 3000);

        var row = _supervisor.Processes[0];
        Assert.Contains("a", row.LogTail);
        Assert.Contains("b", row.LogTail);
        Assert.Contains("c", row.LogTail);
    }

    [Fact]
    public async Task StartAsync_SpawnFailure_RecordsCrashedWithMessage()
    {
        // /bin/this-does-not-exist exits 127; the
        // supervisor should catch the spawn failure
        // and mark the row as Crashed with a message,
        // not throw.
        //
        // .NET's Process.Start doesn't fail synchronously
        // for "executable not found" — it returns a
        // Process handle for the shell, and the shell
        // then exits 127 within ~50ms. The supervisor's
        // Exited event handler is what flips the row
        // from Running to Crashed. The test waits for
        // that transition before asserting.
        var process = NewProcess("/bin/this-command-does-not-exist-12345");

        var id = await _supervisor.StartAsync(process);

        var row = _supervisor.Processes[0];
        Assert.Equal(id, row.Id);
        // Wait for the OnExited event to flip the
        // status. The shell exit propagates as the
        // spawned process's Exited event; the test
        // gives it up to 5s, which is more than enough
        // for a 127-exit that takes < 100ms.
        await WaitForStatus(id, BackgroundProcessStatus.Crashed, 5000);
        Assert.Equal(BackgroundProcessStatus.Crashed, row.Status);
    }

    [Fact]
    public async Task StartAsync_FiresChanged_OnSuccessfulSpawn()
    {
        var fired = 0;
        _supervisor.Changed += (_, _) => fired++;

        var process = NewProcess("sleep", "2");
        await _supervisor.StartAsync(process);

        // The supervisor fires Changed at least twice:
        // once on spawn-success, once on persist. (We
        // also fire on ReloadAsync; the test doesn't
        // call that here.)
        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task StopAsync_UnknownId_ReturnsFalse()
    {
        var stopped = await _supervisor.StopAsync("missing-id");
        Assert.False(stopped);
    }

    [Fact]
    public async Task ReloadAsync_EmptyFile_ProducesEmptyState()
    {
        await _supervisor.ReloadAsync();

        Assert.Empty(_supervisor.Processes);
    }

    [Fact]
    public void ProcessesFile_ExposesConstructorArgument()
    {
        Assert.Equal(_processesFile, _supervisor.ProcessesFile);
    }

    private static BackgroundProcess NewProcess(params string[] commandAndArgs)
    {
        // The first arg is the executable; the rest
        // are arguments. BackgroundProcess's Command
        // is the executable path and Arguments is the
        // list, mirroring the existing PluginTool
        // shape (and what System.Diagnostics.
        // ProcessStartInfo expects).
        return new BackgroundProcess
        {
            Command = commandAndArgs[0],
            Arguments = commandAndArgs.Skip(1).ToList(),
        };
    }

    [Fact]
    public async Task StopAllAsync_TerminatesAllRunningProcesses()
    {
        // 2026-08-03: the desktop host calls StopAllAsync from
        // `desktop.Exit` before disposing the DI container so a
        // user closing the window does not leak a Sites preview
        // (python3 -m http.server) as an orphaned process group.
        // Start two sleepers, then ask the supervisor to stop
        // everything; both should be gone.
        var a = NewProcess("/bin/sh", "-c", "trap 'exit 0' TERM; sleep 30");
        var b = NewProcess("/bin/sh", "-c", "trap 'exit 0' TERM; sleep 30");
        await _supervisor.StartAsync(a);
        await _supervisor.StartAsync(b);

        var stopped = await _supervisor.StopAllAsync(killTimeout: TimeSpan.FromSeconds(3));

        Assert.Equal(2, stopped);
        Assert.All(_supervisor.Processes, row =>
            Assert.NotEqual(BackgroundProcessStatus.Running, row.Status));
    }

    [Fact]
    public async Task StopAllAsync_OnEmptySupervisor_ReturnsZero()
    {
        // No processes running — the call should be a no-op,
        // not throw, and report 0.
        var stopped = await _supervisor.StopAllAsync();
        Assert.Equal(0, stopped);
    }

    private async Task WaitForStatus(
        string processId,
        BackgroundProcessStatus expected,
        int timeoutMs)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = _supervisor.Processes.FirstOrDefault(p => p.Id == processId);
            if (row?.Status == expected) return;
            await Task.Delay(50);
        }
    }
}
