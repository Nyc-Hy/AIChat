using System.IO;
using System.Reflection;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.App.Avalonia.ViewModels;

// Built-in slash commands — ClaudeCode's signature affordance. The user
// types a command starting with '/' in the prompt input; the host
// dispatches here before kicking off an agent run. Each command returns
// a Result the host renders into the activity feed as a system bubble
// (so the response is part of the conversation flow, not just a
// status-bar blip).
//
// The handler takes an ISlashCommandHost so it can read live state
// (active provider / model / current project / conversation count) and
// call the same clear / add / status APIs the rest of the app uses.
// Keeping this in one place means the MainWindowViewModel stays focused
// on cross-VM coordination.
public static class SlashCommandHandler
{
    public sealed record Result(string Title, string Body);

    // /help body lives in Resources/HelpText.md (compiled as an
    // EmbeddedResource, not AvaloniaResource) so the doc can be
    // edited without rebuilding the C# project, and so the
    // markdown rendering path is exercised exactly the way a
    // user-edited doc would be. EmbeddedResource (vs the
    // AvaloniaResource route) is used so the headless test host
    // (AppHost.Build) can still load the body without an
    // Avalonia asset pipeline. The Lazy<string> caches the
    // loaded body across calls.
    private static readonly Lazy<string> HelpBody = new(LoadHelpBody);

    private static string LoadHelpBody()
    {
        // The resource is rooted at the assembly's default
        // namespace, so the full name is
        // AIChat.App.Avalonia.Resources.HelpText.md.
        const string resourceName = "AIChat.App.Avalonia.Resources.HelpText.md";
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' not found. " +
                    "Check the .csproj <EmbeddedResource> entry for Resources\\HelpText.md.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd();
        }
        catch (Exception)
        {
            // If the resource is missing (e.g. tests running
            // without the embedded resource) fall back to a
            // minimal hardcoded string so /help never crashes
            // the handler.
            return "可用命令: /clear · /status · /memory · /git · /copy · /help";
        }
    }

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
    public static async Task<(bool Handled, Result? Result)> TryExecuteAsync(string prompt, ISlashCommandHost host)
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
                return (true, new Result("帮助", HelpBody.Value));
            case "/status":
                return (true, new Result("当前状态", BuildStatus(host)));
            case "/memory":
                return (true, new Result("Memory", BuildMemory(host)));
            case "/git":
            case "/git-status":
                return (true, new Result("Git 状态", await host.GetGitStatusSummaryAsync()));
            case "/copy":
                return (true, await TryCopyLastAssistantAsync(host));
            case "/search":
                return (true, TrySearchConversations(prompt, host));
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
    private static async Task<Result> TryCopyLastAssistantAsync(ISlashCommandHost host)
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

    private static string BuildMemory(ISlashCommandHost host)
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

    private static string BuildStatus(ISlashCommandHost host)
    {
        // SelectedProjectName is "未选择项目" when no project is loaded
        // and "未配置路径" when the project has no on-disk path. Map
        // both to the (未选择) summary so the user gets one consistent
        // "nothing selected" line instead of "项目: 未选择项目" or
        // "项目: 未配置路径" (both read awkwardly).
        var project = host.Sidebar.SelectedProjectName;
        var projectLine = string.IsNullOrWhiteSpace(project) ||
                          project == "未选择项目" ||
                          project == "未配置路径"
            ? "项目: (未选择)"
            : $"项目: {project}";

        var modelLine = string.IsNullOrWhiteSpace(host.ActiveModel)
            ? $"模型: {host.ActiveProvider}"
            : $"模型: {host.ActiveProvider} · {host.ActiveModel}";

        var conversationCount = host.ConversationList.HistoryCount;
        var conversationLine = $"对话数: {conversationCount}";

        // ContextBudgetPercent is the rough fraction of the
        // approximate 64K context window the current run has used.
        // It's the only number the user really needs to decide
        // whether to start a new conversation or compact.
        var contextLine = $"Context: {host.AgentHost.ContextBudgetPercent}%";

        // Safety policy: surface the two user-controlled toggles so
        // the user can verify state without opening settings. Empty
        // by default; only emit a line when the toggle is on so the
        // ambient "everything default" /status output stays tight.
        var safety = new List<string>();
        if (host.NoWriteMode)
        {
            safety.Add("只读");
        }
        if (host.Settings.AutoVerify)
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
        var lastRunLine = string.IsNullOrEmpty(host.AgentHost.LastAssistantStatus)
            ? null
            : $"上次运行: {host.AgentHost.LastAssistantStatus}";

        var lines = new[] { projectLine, modelLine, conversationLine, contextLine, safetyLine, lastRunLine, status }
            .Where(line => line is not null);
        return string.Join("\n", lines);
    }

    // /search <query>: scan every cached ChatSession for matches in
    // title + message content. Renders up to 10 hits with title +
    // first matching message excerpt, so the user can scan the
    // result inline and pick the conversation they want to open.
    // Hit count is reported so a 0-result search is obvious; the
    // daily driver's main use case is 'I had a conversation
    // about <topic> last week, where was it?'.
    //
    // Search is case-insensitive substring on the title and each
    // message's content. Tool-trace arguments + result content
    // are included so a search for a tool name surfaces the
    // conversation that used it. A follow-up slice can add
    // token-level highlighting via Markdown in the result body.
    private static Result TrySearchConversations(string prompt, ISlashCommandHost host)
    {
        var spaceIdx = prompt.IndexOf(' ');
        var query = spaceIdx < 0 ? "" : prompt[(spaceIdx + 1)..].Trim();
        if (string.IsNullOrEmpty(query))
        {
            return new Result("搜索", "用法: `/search <关键词>`，例如 `/search keychain`");
        }

        var hits = new List<(string Title, string Excerpt)>();
        foreach (var session in host.AllSessions)
        {
            if (session.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add((session.Title, "(标题命中)"));
                continue;
            }
            foreach (var message in session.Messages)
            {
                if (message.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var excerpt = ExtractExcerpt(message.Content, query);
                    hits.Add((session.Title, excerpt));
                    break;
                }
                // Tool traces count as content too. A search for
                // "git_commit" should surface conversations that
                // used the git_commit tool, not just ones that
                // typed the literal string.
                foreach (var trace in message.ToolTraces)
                {
                    if ((trace.ToolName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (trace.ArgumentsJson?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (trace.ResultContent?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        var excerpt = $"工具 {trace.ToolName} 调用匹配";
                        hits.Add((session.Title, excerpt));
                        break;
                    }
                }
                if (hits.Count > 0 && hits[^1].Title == session.Title)
                {
                    break;
                }
            }
        }

        if (hits.Count == 0)
        {
            return new Result("搜索", $"没有找到包含「{query}」的对话。");
        }

        const int max = 10;
        var body = new System.Text.StringBuilder();
        body.Append("匹配 ").Append(hits.Count).AppendLine(" 个对话:");
        for (var i = 0; i < Math.Min(hits.Count, max); i++)
        {
            body.Append(i + 1).Append(". **").Append(hits[i].Title).Append("** — ");
            body.AppendLine(hits[i].Excerpt);
        }
        if (hits.Count > max)
        {
            body.Append("（还有 ").Append(hits.Count - max).AppendLine(" 个匹配未显示。缩小关键词以精确查找。）");
        }
        return new Result($"搜索 \"{query}\"", body.ToString());
    }

    // Returns a window of up to 80 characters around the first
    // match of needle in source, with the match surrounded by
    // '…' markers so the user can see the surrounding context.
    private static string ExtractExcerpt(string source, string needle)
    {
        if (string.IsNullOrEmpty(source))
        {
            return "(空消息)";
        }
        var idx = source.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // No match in the message — caller should not have
            // asked for an excerpt. Fall back to the first 60
            // characters so the search result row is not blank.
            return source.Length > 60 ? source[..60] + "…" : source;
        }
        const int radius = 40;
        var start = Math.Max(0, idx - radius);
        var end = Math.Min(source.Length, idx + needle.Length + radius);
        var prefix = start > 0 ? "…" : "";
        var suffix = end < source.Length ? "…" : "";
        return prefix + source[start..end] + suffix;
    }
}
