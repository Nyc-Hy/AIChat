using System.Text.Json.Serialization;

namespace AIChat.Domain.Scheduled;

// Wave 9 (parity plan §7 Wave 9): one row in the
// "已安排" (Scheduled) panel. A ScheduledTask is a saved
// agent prompt that the user wants to re-run on a cadence
// — daily standup notes, weekly dependency audit, etc.
//
// First slice: this is a data model + UI surface only. The
// cron / scheduler engine that actually fires these on a
// timer lands in a follow-up slice; for now the user
// triggers a task with "立即运行" and the recorded cadence
// is metadata that drives the "上次 / 下次" display.
//
// Approval-on-no-human-interaction is the hard rule from
// plan §7 Wave 9 — a scheduled run that lands on a tool
// requiring approval must fail explicitly instead of
// silently auto-granting. The runner checks `IsBackground`
// on the run and routes through `IApprovalService` which
// surfaces a "需要审批 (无人值守)" failure when no
// approval is given within the timeout. That wiring is the
// runner's job (see AgentRunnerViewModel), not the
// domain's — this file just carries the data shape.
public sealed class ScheduledTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // Project id (`WorkspaceProject.Id`) this task runs
    // against. Empty string means "no project" — the user
    // can use this for a Standalone-style scheduled prompt
    // (the runner falls back to the active Standalone
    // session in that case).
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("cadence")]
    public ScheduledCadence Cadence { get; set; } = ScheduledCadence.Manual;

    // HH:mm in the user's local timezone. Ignored when
    // cadence is Manual.
    [JsonPropertyName("cadenceTime")]
    public string CadenceTime { get; set; } = "09:00";

    [JsonPropertyName("executionEnvironment")]
    public ScheduledExecutionEnvironment ExecutionEnvironment { get; set; } =
        ScheduledExecutionEnvironment.Local;

    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    // When this task last completed a run (any status).
    // Used to drive the "上次 / 下次" display. The
    // scheduler engine writes this when a run lands.
    [JsonPropertyName("lastRunAt")]
    public DateTimeOffset? LastRunAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// The cadence enum the user picks in the create form.
// Mirrors the visible options in the Codex "Schedule a
// task" modal: Once (run now, never again) / Daily /
// Weekly (Mon-Fri) / Manual (only on user click). The
// "every N hours / cron expression" power-user shape is
// deferred — the user-visible "Daily 09:00" surface is
// the parity item the plan calls out.
public enum ScheduledCadence
{
    Manual = 0,
    Once = 1,
    Daily = 2,
    Weekly = 3,
}

// Execution environment picks. Plan §7 Wave 9 says
// "Local 与 Dedicated Worktree". Dedicated Worktree
// requires `git worktree` plumbing (Wave 6 follow-up);
// for now both values round-trip through persistence
// and the runner treats Worktree as a marker that the
// follow-up slice will route through. AIChat's Codex
// parity scope only requires the surface — the worktree
// enforcement is the same Wave 6 follow-up that handles
// the Git tab worktree picker.
public enum ScheduledExecutionEnvironment
{
    Local = 0,
    DedicatedWorktree = 1,
}

// One historical run of a ScheduledTask. Append-only —
// the list grows forever; archival lives in a follow-up
// slice that adds an "archive" filter to the history
// view. The current display lists every run in the task
// panel, newest first.
public sealed class ScheduledTaskRun
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("scheduledTaskId")]
    public string ScheduledTaskId { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("status")]
    public ScheduledRunStatus Status { get; set; } = ScheduledRunStatus.Running;

    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public enum ScheduledRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    ApprovalRequired = 3,
    Cancelled = 4,
}
