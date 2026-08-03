using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.App.Avalonia.ViewModels;

// Narrow surface the slash command handler depends on. Pulled out
// of MainWindowViewModel so the handler no longer takes the whole
// top-level VM as a parameter (the previous shape meant every new
// field on MainWindowViewModel tempted someone to "just read it
// here" and the dependency grew one property at a time).
//
// Today MainWindowViewModel is the only implementation. Future
// callers (e.g. a CLI host, an in-process test fixture) can
// implement the interface without standing up the whole Avalonia
// DI graph.
public interface ISlashCommandHost
{
    // ---- Sub-VMs the handler reads through (read-only) ----

    ActivityFeedViewModel ActivityFeed { get; }

    ProjectSidebarViewModel Sidebar { get; }

    ConversationListViewModel ConversationList { get; }

    AgentHostViewModel AgentHost { get; }

    SettingsViewModel Settings { get; }

    // ---- Host-level display state (read + write) ----

    string StatusMessage { get; set; }

    string ActiveProvider { get; }

    string ActiveModel { get; }

    bool NoWriteMode { get; }

    // /search: the full cross-project session list, regardless
    // of which project is currently active. MainWindowViewModel
    // caches the last-loaded IReadOnlyList<ChatSession> from
    // LoadSessionsAsync so this stays an O(N) read on the cached
    // collection — a full disk load on every /search would be
    // wasteful for a daily-driver with 100+ sessions.
    IReadOnlyList<ChatSession> AllSessions { get; }

    // ---- Side-effect actions ----

    // /copy: write the last assistant bubble's text to the system
    // clipboard. The platform clipboard isn't always available
    // (no TopLevel in unit tests) so the host reports
    // availability through HasClipboardService and the handler
    // checks before issuing the call.
    bool HasClipboardService { get; }

    Task CopyToClipboardAsync(string text);

    // /git (and /git-status): render the current project's branch
    // + change list as a single string the handler can drop into
    // a system bubble. Heavy lifting lives in the workspace change
    // service; this is just the read path through the host.
    Task<string> GetGitStatusSummaryAsync();
}
