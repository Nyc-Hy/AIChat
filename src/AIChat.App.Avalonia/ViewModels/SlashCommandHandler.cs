using AIChat.Domain.Projects;

namespace AIChat.App.Avalonia.ViewModels;

// Built-in slash commands — ClaudeCode's signature affordance. The user
// types a command starting with '/' in the prompt input; the host
// dispatches here before kicking off an agent run. Each command returns
// a Result the host renders into the activity feed as a system bubble
// (so the response is part of the conversation flow, not just a
// status-bar blip).
//
// The handler takes the host as a dependency so it can read live state
// (active provider / model / current project / conversation count) and
// call the same clear / add / status APIs the rest of the app uses.
// Keeping this in one place means the MainWindowViewModel stays focused
// on cross-VM coordination.
public static class SlashCommandHandler
{
    public sealed record Result(string Title, string Body);

    private const string HelpBody =
        "可用命令:\n" +
        "/clear — 清空当前对话\n" +
        "/new — 同 /clear\n" +
        "/status — 显示当前项目、模型、对话数\n" +
        "/help — 显示本帮助";

    // Returns true if the prompt was a slash command and the host
    // should skip the normal agent flow. The out Result is the text
    // the host renders as a system bubble in the activity feed; null
    // means the handler already performed a side effect (e.g. /clear
    // wiped the feed) and there's nothing to render.
    public static bool TryExecute(string prompt, MainWindowViewModel host, out Result? result)
    {
        result = null;
        if (string.IsNullOrEmpty(prompt) || prompt[0] != '/')
        {
            return false;
        }

        var spaceIdx = prompt.IndexOf(' ');
        var command = (spaceIdx < 0 ? prompt : prompt[..spaceIdx]).ToLowerInvariant();

        switch (command)
        {
            case "/clear":
            case "/new":
                // Wipe the activity feed (the user's expectation is "the
                // conversation is gone", not "there's a new bubble
                // announcing that it's gone"). Result stays null so the
                // host doesn't echo anything into the now-empty feed.
                host.ActivityFeed.Clear();
                host.StatusMessage = "已清空对话。";
                return true;
            case "/help":
                result = new Result("帮助", HelpBody);
                return true;
            case "/status":
                result = new Result("当前状态", BuildStatus(host));
                return true;
            default:
                result = new Result(
                    "未知命令",
                    $"没有 `{command}` 这个命令。输入 `/help` 查看可用命令。");
                return true;
        }
    }

    private static string BuildStatus(MainWindowViewModel host)
    {
        var project = host.Sidebar.SelectedProjectName;
        var projectLine = string.IsNullOrWhiteSpace(project) || project == "未配置路径"
            ? "项目: (未选择)"
            : $"项目: {project}";

        var modelLine = string.IsNullOrWhiteSpace(host.ActiveModel)
            ? $"模型: {host.ActiveProvider}"
            : $"模型: {host.ActiveProvider} · {host.ActiveModel}";

        var conversationCount = host.ConversationList.Conversations.Count;
        var conversationLine = $"对话数: {conversationCount}";

        var status = string.IsNullOrWhiteSpace(host.StatusMessage)
            ? "状态: 就绪"
            : $"状态: {host.StatusMessage}";

        return string.Join("\n", projectLine, modelLine, conversationLine, status);
    }
}
