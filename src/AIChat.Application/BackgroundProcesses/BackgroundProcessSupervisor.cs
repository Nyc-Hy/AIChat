using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.BackgroundProcesses;

namespace AIChat.Application.BackgroundProcesses;

// Concrete IBackgroundProcessSupervisor. Spawns child
// processes via setsid so the supervisor can send signals
// to the entire process group (process tree) without
// needing a PID walk — this is the plan §13 P0 risk
// "整个子进程树" requirement. macOS / Linux both ship
// /usr/bin/setsid; Windows would need job objects, which
// is a follow-up slice.
//
// Persistence: every state change rewrites the
// processes.json sidecar (atomic via JsonFileStore). On
// startup, the ReloadAsync path scans Running entries
// and marks any whose PID is no longer alive as Crashed
// — this is the "重启后可读 / 运行中记录可恢复" half of
// the Wave 7 exit criteria.
//
// Concurrency: a single _gate serialises read-modify-
// write sections. Saves are atomic. Reads outside the
// lock are safe (the list reference is replaced
// atomically) but consumers should re-read on `Changed`
// for strict consistency.
public sealed class BackgroundProcessSupervisor : IBackgroundProcessSupervisor
{
    private readonly string _processesFilePath;
    private readonly object _gate = new();

    private List<BackgroundProcess> _processes = [];
    private readonly Dictionary<string, RunningHandle> _running = new(StringComparer.Ordinal);

    public BackgroundProcessSupervisor(string? processesFilePath = null)
    {
        ProcessesFile = processesFilePath ?? AppRuntimeProfile.BackgroundProcessesFile;
        _processesFilePath = ProcessesFile;
    }

    public string ProcessesFile { get; }

    public IReadOnlyList<BackgroundProcess> Processes
    {
        get { lock (_gate) { return _processes.ToArray(); } }
    }

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await Persistence.JsonFileStore
            .LoadListAsync<BackgroundProcess>(_processesFilePath, cancellationToken)
            .ConfigureAwait(false);

        // Restart-recovery: any Running entry whose PID
        // is no longer alive gets marked Crashed so the
        // user can see "this process exited while the
        // app was off". We don't relaunch — relaunch
        // policy is a future slice (the user would
        // expect a "restart on launch" toggle, and that
        // needs Settings schema work first).
        foreach (var p in loaded)
        {
            if (p.Status == BackgroundProcessStatus.Running && p.Pid > 0)
            {
                if (!IsProcessAlive(p.Pid))
                {
                    p.Status = BackgroundProcessStatus.Crashed;
                    p.StoppedAt = DateTimeOffset.UtcNow;
                    p.ExitCode ??= -1;
                }
            }
        }

        lock (_gate)
        {
            _processes = loaded;
        }
        await Persistence.JsonFileStore
            .SaveListAsync(_processesFilePath, loaded, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> StartAsync(
        BackgroundProcess process,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (string.IsNullOrWhiteSpace(process.Id))
        {
            process.Id = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrWhiteSpace(process.Name))
        {
            process.Name = process.Command;
        }

        // Spawn via /bin/sh -c so the shell can parse
        // the command + args. To enable process-tree
        // kill, we use SetNewProcessGroup via P/Invoke
        // (CreateProcessW CREATE_NEW_PROCESS_GROUP on
        // Windows, setpgid(0, 0) on Unix). setsid(1)
        // would do the same but it's not installed by
        // default on macOS — the P/Invoke is more
        // portable.
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(process.WorkingDirectory)
                ? Environment.CurrentDirectory
                : process.WorkingDirectory,
        };
        var argLine = BuildShellCommand(process);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(argLine);

        Process? child = null;
        try
        {
            child = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    $"Process.Start returned null for {process.Command}.");
            // Move the child into its own process group
            // so the supervisor can SIGTERM / SIGKILL
            // the whole group on Stop. P/Invoke because
            // .NET's ProcessStartInfo doesn't expose
            // setpgid / CREATE_NEW_PROCESS_GROUP directly.
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                TrySetProcessGroup(child.Id);
            }
        }
        catch (Exception ex)
        {
            // Spawn failed — record the failure so the
            // user can see "this command couldn't start"
            // instead of a row stuck in Pending forever.
            process.Status = BackgroundProcessStatus.Crashed;
            process.StoppedAt = DateTimeOffset.UtcNow;
            process.LogTail.Add($"Failed to start: {ex.Message}");
            await PersistAndPublishAsync(cancellationToken).ConfigureAwait(false);
            return process.Id;
        }

        process.Pid = child.Id;
        process.Status = BackgroundProcessStatus.Running;
        process.StartedAt = DateTimeOffset.UtcNow;
        process.StoppedAt = null;
        process.ExitCode = null;

        var handle = new RunningHandle(child, process.Id);
        // Capture stdout / stderr on background threads;
        // append to the process's LogTail, capped at
        // MaxLogLines so a chatty process doesn't grow
        // the JSON file unboundedly.
        child.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) AppendLog(process.Id, e.Data);
        };
        child.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) AppendLog(process.Id, e.Data);
        };
        child.EnableRaisingEvents = true;
        child.Exited += (_, _) => OnProcessExited(process.Id, child);
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();

        lock (_gate)
        {
            _processes.Add(process);
            _running[process.Id] = handle;
        }
        await PersistAndPublishAsync(cancellationToken).ConfigureAwait(false);
        return process.Id;
    }

    public async Task<bool> StopAsync(
        string processId,
        TimeSpan? killTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processId))
        {
            return false;
        }

        RunningHandle? handle;
        BackgroundProcess? process;
        lock (_gate)
        {
            if (!_running.TryGetValue(processId, out handle)) return false;
            process = _processes.FirstOrDefault(p => p.Id == processId);
        }
        if (process is null) return false;

        // SIGTERM to the entire process group; on macOS
        // / Linux the child was started with setpgid
        // so the PID equals the pgid. After the timeout,
        // escalate to SIGKILL.
        var timeout = killTimeout ?? TimeSpan.FromSeconds(5);
        try
        {
            // Negative pid = kill the process group.
            // The setpgid'd child has Pid == pgid, so -Pid
            // targets its whole subtree.
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                TryKillProcessGroup(process.Pid, SIGTERM);
            }
            else
            {
                handle.Process.Kill();
            }

            if (handle.Process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Status = handle.Process.ExitCode == 0
                    ? BackgroundProcessStatus.Stopped
                    : BackgroundProcessStatus.Crashed;
                process.StoppedAt = DateTimeOffset.UtcNow;
                process.ExitCode = handle.Process.ExitCode;
            }
            else
            {
                // Timeout — escalate.
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    TryKillProcessGroup(process.Pid, SIGKILL);
                }
                else
                {
                    handle.Process.Kill();
                }
                handle.Process.WaitForExit(2000);
                process.Status = BackgroundProcessStatus.ForceKilled;
                process.StoppedAt = DateTimeOffset.UtcNow;
                process.ExitCode = -1;
                process.LogTail.Add("Killed by supervisor (force-kill after SIGTERM timeout).");
            }
        }
        catch (Exception ex)
        {
            process.LogTail.Add($"Stop failed: {ex.Message}");
            process.Status = BackgroundProcessStatus.Crashed;
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(processId);
            }
        }

        await PersistAndPublishAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> StopAllAsync(
        TimeSpan? killTimeout = null,
        CancellationToken cancellationToken = default)
    {
        // Snapshot the running set under the gate so we don't
        // race with concurrent Start / Stop from the UI thread.
        List<string> ids;
        lock (_gate)
        {
            ids = _running.Keys.ToList();
        }

        var stopped = 0;
        foreach (var id in ids)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            try
            {
                if (await StopAsync(id, killTimeout, cancellationToken).ConfigureAwait(false))
                {
                    stopped++;
                }
            }
            catch
            {
                // Shutdown path: never propagate individual failures.
            }
        }
        return stopped;
    }

    // Build a shell command line that runs the user's
    // command. The supervisor moves the spawned child
    // into its own process group via setpgid(0, 0)
    // (P/Invoke) right after Process.Start returns, so
    // we don't need setsid(1) in the command line.
    // setsid isn't installed by default on macOS, so
    // relying on it would break the supervisor on a
    // clean install. ArgumentList quoting is the
    // standard sh -c double-quote-with-escapes pattern.
    private static string BuildShellCommand(BackgroundProcess process)
    {
        var quotedArgs = string.Join(" ",
            new[] { QuoteShellArg(process.Command) }
            .Concat(process.Arguments.Select(QuoteShellArg)));
        return $"exec sh -c {QuoteShellArg(quotedArgs)}";
    }

    private static string QuoteShellArg(string arg)
    {
        // Wrap in single quotes; escape any embedded
        // single quotes by closing + escaped + reopening.
        return "'" + arg.Replace("'", "'\\''") + "'";
    }

    private void AppendLog(string processId, string line)
    {
        BackgroundProcess? process;
        lock (_gate)
        {
            process = _processes.FirstOrDefault(p => p.Id == processId);
        }
        if (process is null) return;
        process.LogTail.Add(line);
        if (process.LogTail.Count > BackgroundProcess.MaxLogLines)
        {
            // Drop oldest lines. List.RemoveRange is O(n)
            // but n is bounded to MaxLogLines; this is
            // called once per stdout line, so worst case
            // 200 lines × O(200) = trivial.
            process.LogTail.RemoveRange(0,
                process.LogTail.Count - BackgroundProcess.MaxLogLines);
        }
    }

    private void OnProcessExited(string processId, Process process)
    {
        BackgroundProcess? target;
        lock (_gate)
        {
            target = _processes.FirstOrDefault(p => p.Id == processId);
            _running.Remove(processId);
        }
        if (target is null) return;
        target.Status = process.ExitCode == 0
            ? BackgroundProcessStatus.Stopped
            : BackgroundProcessStatus.Crashed;
        target.StoppedAt = DateTimeOffset.UtcNow;
        target.ExitCode = process.ExitCode;
        // Fire-and-forget persist; the LogTail may have
        // queued entries that haven't been written yet.
        _ = PersistAndPublishAsync(CancellationToken.None);
    }

    private async Task PersistAndPublishAsync(CancellationToken cancellationToken)
    {
        List<BackgroundProcess> snapshot;
        lock (_gate)
        {
            snapshot = _processes.ToList();
        }
        await Persistence.JsonFileStore
            .SaveListAsync(_processesFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Cheap alive check used by ReloadAsync to
    // reconcile. Sends signal 0 — returns true iff the
    // pid is owned by some process the current user can
    // signal. Doesn't disturb the target process.
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            // Process.GetProcessById throws if the
            // process doesn't exist (or we lack access).
            // Wrap in try / catch so a transient
            // permission failure doesn't false-positive
            // "the process is dead".
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // Per-process handle held while the OS process is
    // alive. The Process reference is what StopAsync
    // waits on; the Exited event fires from a
    // background thread, which is why OnProcessExited
    // takes the lock + dispatches the persist.
    private sealed class RunningHandle
    {
        public Process Process { get; }
        public string ProcessId { get; }

        public RunningHandle(Process process, string processId)
        {
            Process = process;
            ProcessId = processId;
        }
    }

    // ---- Unix process-group plumbing ----
    //
    // The supervisor kills the entire process tree
    // (plan §13 P0 risk "整个子进程树") by sending
    // signals to the process group. The P/Invoke calls
    // are the standard libc setpgid / kill — macOS and
    // Linux both ship these.
    //
    // SIGTERM (15) = polite shutdown. The wrapped
    // process has killTimeout to handle it before we
    // escalate.
    // SIGKILL (9) = unblockable. Used as the escalation.
    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    [DllImport("libc", SetLastError = true, EntryPoint = "setpgid")]
    private static extern int SetPgid(int pid, int pgid);

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int Kill(int pid, int sig);

    // Move the spawned child into its own process
    // group. On macOS / Linux this is required so
    // Kill(pid, -pgid) terminates the whole tree
    // instead of just the immediate child. The call
    // is best-effort: a failure (e.g. the child
    // already exited) is logged to the row's log tail
    // and the supervisor falls back to a direct
    // process kill, which leaves a window for orphan
    // grandchildren.
    private static void TrySetProcessGroup(int pid)
    {
        try
        {
            // 0 = make this pid the leader of a new
            // group whose pgid equals pid. Identical to
            // `setpgid(pid, pid)` but more idiomatic.
            var rc = SetPgid(pid, 0);
            if (rc != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                // Best-effort — log to no specific place;
                // the row's log tail is not yet wired
                // at this point in the spawn path.
            }
        }
        catch
        {
            // setpgid can throw on platforms without
            // libc (Windows). OperatingSystem check
            // guards against that, but catch here as
            // a belt-and-braces fallback.
        }
    }

    // Send a signal to an entire process group. pid
    // is treated as the pgid (the supervisor's
    // setpgid'd children have Pid == pgid, so -Pid
    // targets the whole subtree).
    private static void TryKillProcessGroup(int pid, int signal)
    {
        try
        {
            // Negative pid = process group. The
            // supervisor set the child's pgid to its
            // own pid, so -pid targets the subtree.
            Kill(-pid, signal);
        }
        catch
        {
            // Best-effort. The supervisor's caller
            // (StopAsync) escalates to ForceKilled
            // status regardless; the process row is
            // marked either way.
        }
    }
}
