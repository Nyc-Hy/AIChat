namespace AIChat.Abstractions.Configuration;

// Process-level data-path policy. Normal launches keep using the platform
// application-data folder and the OS credential vault. Setting the explicit
// isolated root gives UI tests, demos, and support sessions a clean profile
// that never opens the user's production settings or credential vault.
public static class AppRuntimeProfile
{
    public const string IsolatedDataRootEnvironmentVariable = "AICHAT_ISOLATED_DATA_ROOT";

    public static string? IsolatedDataRoot
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(IsolatedDataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return null;
            }
            return ResolveDataDirectory(configured);
        }
    }

    public static bool IsIsolated => IsolatedDataRoot is not null;

    public static string DataDirectory => IsolatedDataRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AIChat");

    public static string ResolveDataDirectory(string isolatedDataRoot)
    {
        if (string.IsNullOrWhiteSpace(isolatedDataRoot) ||
            !Path.IsPathFullyQualified(isolatedDataRoot))
        {
            throw new InvalidOperationException(
                $"{IsolatedDataRootEnvironmentVariable} 必须是绝对路径。");
        }

        return Path.GetFullPath(isolatedDataRoot);
    }

    public static string ArtifactsDirectory => Path.Combine(DataDirectory, "artifacts");

    public static string PendingAttachmentsDirectory =>
        Path.Combine(DataDirectory, "pending-attachments");

    // Wave 8 (parity plan §7 Wave 8): local plugin manifests
    // (one `plugin.json` per plugin under a subdirectory) live
    // here. The PluginRegistry scans this directory on startup
    // and after every Install / Uninstall / Reload. Each plugin
    // gets its own subdirectory; the directory is created
    // lazily on first read.
    public static string PluginsDirectory => Path.Combine(DataDirectory, "plugins");

    // Wave 9 (parity plan §7 Wave 9): user-saved Scheduled
    // tasks. One JSON file (scheduled-tasks.json) holds the
    // full list of tasks; the file is small (handful of
    // rows) and rewritten atomically on every mutation. Run
    // history is appended to scheduled-task-runs.json.
    public static string ScheduledTasksFile => Path.Combine(DataDirectory, "scheduled-tasks.json");
    public static string ScheduledTaskRunsFile => Path.Combine(DataDirectory, "scheduled-task-runs.json");

    // Wave 9 (parity plan §7 Wave 9): user-saved Sites.
    // Same shape as Scheduled — sites.json holds the list of
    // sites; site-deployments.json holds the per-site
    // history. Local-preview state (port allocation, last
    // preview time) lives on the Site row itself.
    public static string SitesFile => Path.Combine(DataDirectory, "sites.json");
    public static string SiteDeploymentsFile => Path.Combine(DataDirectory, "site-deployments.json");

    // Wave 7 (parity plan §7 Wave 7): user-captured data
    // sources. sources.json holds the list (clipboard
    // snapshots, web fetches, connector imports). Small
    // file, rewritten atomically on every mutation.
    public static string SourcesFile => Path.Combine(DataDirectory, "sources.json");

    // Wave 7 follow-up: BackgroundProcessSupervisor
    // persists the running process list to this file
    // on every state change. Restart-recovery walks
    // the file, marks any "Running" rows whose PID is
    // no longer alive as Crashed, and re-renders the
    // Environment panel's Background section.
    public static string BackgroundProcessesFile => Path.Combine(DataDirectory, "background-processes.json");

    // 2026-08-03: append-only log of unhandled exceptions
    // (AppDomain / Dispatcher / TaskScheduler). The
    // CrashReporter writes here; the host shows a one-time
    // toast after restart if a new entry was added since
    // the last run. Users can read it directly to attach
    // to a bug report — no telemetry, no upload.
    public static string CrashLogFile => Path.Combine(DataDirectory, "crash.log");
}
