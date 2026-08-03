using AIChat.App.Avalonia.ViewModels;

namespace AIChat.App.Avalonia.Views;

// One-stop helper for keyboard shortcuts that re-route through a
// slash command. The MainWindow code-behind used to inline the same
// 3-step pattern for ⌘⇧C (/copy) and ⌘G (/git): call
// SlashCommandHandler.TryExecuteAsync, drop the
// returned Result as a system bubble in the activity feed, then
// push the same title to the status bar.
//
// Centralising here means the shortcut surface and the /slash
// surface stay in lockstep — if we later change how a slash
// result is rendered (e.g. a new toast level, a new bubble
// category) it lands in one place instead of three identical
// lambda blocks scattered through the ctor.
internal static class KeyCommandBridge
{
    // Run a slash command and surface its Result as both a system
    // bubble in the activity feed and a status-bar message. Used
    // by ⌘⇧C / ⌘G to mirror the user-typed /slash path.
    //
    // The bubbleCategory lets the call site pick the visual
    // flavour — /git lands as a "系统" bubble (read-only
    // information), /copy lands as "命令" (a user-initiated
    // action that did something). Default "命令" matches what
    // the AgentHost.SendTaskAsync path does when it routes a
    // user-typed slash through the same handler.
    public static async Task RunSlashCommandAsync(
        ISlashCommandHost host,
        string command,
        string bubbleCategory = "命令")
    {
        var (handled, result) = await SlashCommandHandler.TryExecuteAsync(command, host);
        if (handled && result is not null)
        {
            host.ActivityFeed.Add(result.Title, result.Body, bubbleCategory);
            host.StatusMessage = result.Title + "。";
        }
    }
}
