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
        "/status — 显示当前项目、模型、对话数、Context、上次运行、安全策略\n" +
        "/memory — 显示当前项目的 memory 列表\n" +
        "/git — 显示当前项目的 git 状态\n" +
        "/copy — 复制最后一条 AI 回复到剪贴板\n" +
        "/help — 显示本帮助";

    // Returns true if the prompt was a slash command and the host
    // should skip the normal agent flow. The returned Result is the text
    // the host renders as a system bubble in the activity feed; null
    // means the handler already performed a side effect (e.g. /clear
    // wiped the feed) and there's nothing to render.
    //
    // Returns a value tuple because async methods can't take `out`
    // parameters (CS1988). Callers destructure as
    // `var (handled, result) = await SlashCommandHandler.TryExecuteAsync(...)`.
    //
    // Async because /copy needs to await the platform clipboard call
    // before it can report success. The other commands still run
    // synchronously — they simply return Task.CompletedTask.
    public static async Task<(bool Handled, Result? Result)> TryExecuteAsync(string prompt, MainWindowViewModel host)
    {
        if (string.IsNullOrEmpty(prompt) || prompt[0] != '/')
        {
            return (false, null);
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
                return (true, null);
            case "/help":
                return (true, new Result("帮助", HelpBody));
            case "/status":
                return (true, new Result("当前状态", BuildStatus(host)));
            case "/memory":
                return (true, new Result("Memory", BuildMemory(host)));
            case "/git":
            case "/git-status":
                return (true, new Result("Git 状态", await host.GetGitStatusSummaryAsync()));
            case "/copy":
                return (true, await TryCopyLastAssistantAsync(host));
            default:
                return (true, new Result(
                    "未知命令",
                    $"没有 `{command}` 这个命令。输入 `/help` 查看可用命令。"));
        }
    }

    // /copy: find the most recent assistant bubble and put its text on the
    // clipboard. Reports success or failure back to the host as a Result
    // so the user sees a system bubble confirming what just happened
    // (avoids the silent-success trap where a copy silently failed and
    // the user later wonders why their paste is empty).
    private static async Task<Result> TryCopyLastAssistantAsync(MainWindowViewModel host)
    {
        var last = host.ActivityFeed.Activity.LastOrDefault(item => item.IsAssistantBubble);
        if (last is null)
        {
            return new Result("复制", "当前对话没有可复制的 AI 消息。");
        }

        var text = last.Detail;
        if (string.IsNullOrEmpty(text))
        {
            return new Result("复制", "最后一条 AI 消息还是空的。");
        }

        if (!host.HasClipboardService)
        {
            return new Result("复制失败", "剪贴板不可用 (TopLevel 未就绪)。");
        }

        await host.CopyToClipboardAsync(text);
        var preview = text.Length > 60 ? text[..60].Replace('\n', ' ') + "…" : text.Replace('\n', ' ');
        return new Result("已复制", $"{text.Length} 字符: {preview}");
    }

    private static string BuildMemory(MainWindowViewModel host)
    {
        var project = host.Sidebar.CurrentProject;
        if (project is null || project.Memories.Count == 0)
        {
            return "(当前项目还没有 memory 记录)";
        }

        var lines = new List<string>
        {
            $"{project.Memories.Count} 条记录 (项目: {project.Name}):"
        };

        // Group by category so the user can scan the rough shape of
        // what the agent has remembered. Sorted by UpdatedAt descending
        // inside each group so the most recent items surface first.
        var byCategory = project.Memories
            .GroupBy(item => item.Category)
            .OrderBy(group => group.Key);

        foreach (var group in byCategory)
        {
            lines.Add("");
            lines.Add($"[{group.Key}]");
            foreach (var item in group.OrderByDescending(item => item.UpdatedAt).Take(5))
            {
                var truncated = item.Content.Length > 120
                    ? item.Content[..120] + "…"
                    : item.Content;
                lines.Add($"  • {truncated}");
            }
            if (group.Count() > 5)
            {
                lines.Add($"  … 还有 {group.Count() - 5} 条");
            }
        }

        return string.Join("\n", lines);
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

        // ContextBudgetPercent is the rough fraction of the
        // approximate 64K context window the current run has used.
        // It's the only number the user really needs to decide
        // whether to start a new conversation or compact.
        var contextLine = $"Context: {host.ContextBudgetPercent}%";

        // Safety policy: surface the two user-controlled toggles so
        // the user can verify state without opening settings. Empty
        // by default; only emit a line when the toggle is on so the
        // ambient "everything default" /status output stays tight.
        var safety = new List<string>();
        if (host.NoWriteMode)
        {
            safety.Add("只读");
        }
        if (host.AutoVerify)
        {
            safety.Add("自动验证");
        }
        var safetyLine = safety.Count == 0 ? null : "安全策略: " + string.Join(" + ", safety);

        var status = string.IsNullOrWhiteSpace(host.StatusMessage)
            ? "状态: 就绪"
            : $"状态: {host.StatusMessage}";

        // Last run's terminal status is more actionable than the current
        // StatusMessage — the user typically wants to know "what just
        // happened" not "what's the latest update". Only emit the line
        // when there has been at least one run.
        var lastRunLine = string.IsNullOrEmpty(host.LastAssistantStatus)
            ? null
            : $"上次运行: {host.LastAssistantStatus}";

        var lines = new[] { projectLine, modelLine, conversationLine, contextLine, safetyLine, lastRunLine, status }
            .Where(line => line is not null);
        return string.Join("\n", lines);
    }
}
