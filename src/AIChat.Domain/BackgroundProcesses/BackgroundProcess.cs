using System.Text.Json.Serialization;

namespace AIChat.Domain.BackgroundProcesses;

// Wave 7 follow-up (parity plan §7 Wave 7 + §13 P0 risk
// "后台进程成为孤儿"): one row in the Background
// Processes section. A process is anything long-lived
// the user wants supervised — a dev server, a build
// watcher, a Sites local preview, a scheduled run's
// background poll. The supervisor tracks lifecycle
// (PID, status, exit code) and persists so the app
// can reconcile the on-disk state with what's still
// running after a restart.
//
// The model mirrors ScheduledTask's shape on purpose:
// both are "supervised side-effects" that outlive a
// single user action, both persist to a sidecar JSON
// file, both expose a single history list. Sites
// local preview and Scheduled runs would be
// represented as BackgroundProcess entries in a
// future slice; for now this file is the foundation.
public sealed class BackgroundProcess
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = [];

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    // Process ID. 0 = not running. Populated when the
    // supervisor calls Process.Start, cleared when the
    // process exits. Used by the supervisor's Stop
    // method to send the kill signal.
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("status")]
    public BackgroundProcessStatus Status { get; set; } = BackgroundProcessStatus.Pending;

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("stoppedAt")]
    public DateTimeOffset? StoppedAt { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    // Last few lines of stdout / stderr. Capped at
    // MaxLogLines so a chatty process doesn't grow the
    // JSON file unboundedly. Surfaced in the UI as a
    // tail view (full log is a follow-up slice).
    [JsonPropertyName("logTail")]
    public List<string> LogTail { get; set; } = [];

    public const int MaxLogLines = 200;
}

public enum BackgroundProcessStatus
{
    // Not yet started. Default for a freshly added
    // process. The supervisor flips this to Running on
    // StartAsync.
    Pending = 0,

    // Spawn succeeded; the OS process is alive.
    Running = 1,

    // StopAsync was called and the process exited
    // cleanly (or was killed). ExitCode carries the
    // value the process reported.
    Stopped = 2,

    // The process exited unexpectedly with a non-zero
    // exit code, or crashed (segfault, OOM kill, etc).
    // The supervisor's restart-recovery logic flags
    // Running entries as Crashed on startup if the PID
    // is no longer alive.
    Crashed = 3,

    // StopAsync was called but the process refused to
    // exit within the kill timeout. The supervisor
    // escalated to SIGKILL; this status means the
    // process is gone but the user should know it
    // didn't go quietly.
    ForceKilled = 4,
}
