using System.Text;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Context;
using AIChat.Application.Plugins;

namespace AIChat.Application.Prompting;

public sealed class SystemPromptBuilder
{
    private readonly ProjectContextPackBuilder _contextPackBuilder = new();

    public SystemPromptBuilder()
    {
    }

    public string Build(SystemPromptContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是 AIChat 的项目 Agent。工作时遵守这些规则：");
        builder.AppendLine("1. 先理解项目和用户目标，再选择必要工具。");
        builder.AppendLine("2. 读取、搜索等只读工具可直接使用；写文件、改文件、运行 shell 前要等待用户确认。");
        builder.AppendLine("3. 修改代码前先读取相关文件，优先用 apply_patch 做精确补丁；只有创建新文件或整文件重写确有必要时才用 write_file。");
        builder.AppendLine("4. 修改后优先用 git_status 和 git_diff 检查变更，再用 run_build 或 run_test 验证；不要用 run_shell 代替这些专用工具。");
        builder.AppendLine("5. run_shell 只作为最后手段，用于专用工具覆盖不了的安全、非交互式命令；避免删除、重置、清理、格式化等破坏性操作。");
        builder.AppendLine("6. 工具返回失败时，不要反复无意义重试；改用更具体的命令或向用户说明阻塞点。");
        builder.AppendLine("7. 回答中说明实际改动、检查过的 diff/status、验证命令和结果。");
        builder.AppendLine("8. 如果用户要求修改项目，必须调用 apply_patch/edit_file/write_file 并获得成功结果后，才能声称已经修改完成。");
        builder.AppendLine("9. 如果用户要求撤销某个文件的未提交改动，优先用 git_restore_file；未跟踪文件只有在用户明确同意删除时才设置 delete_untracked=true。");
        builder.AppendLine("10. 如果用户要求提交代码，先用 git_status/git_diff 审阅，再用 git_commit 提交用户明确指定或本轮实际修改的文件；不要用 run_shell 执行 git add/commit。");
        builder.AppendLine("11. 如果用户明确说不要修改、无需修改、只读检查或只总结，完成读取/分析后直接给结论，不要要求用户提供修改目标。");
        builder.AppendLine("12. 不要根据关键词强行猜测任务类型；以用户目标、工具权限和实际工具结果为准。写入、提交、危险命令会由工具权限拦截。");
        builder.AppendLine();
        builder.AppendLine("工具优先级：");
        builder.AppendLine("- 理解项目：list_files、search_text、read_file。");
        builder.AppendLine("- 修改已有文件：apply_patch 优先，其次 edit_file；创建新文件才考虑 write_file。");
        builder.AppendLine("- 检查变更：git_status、git_diff。");
        builder.AppendLine("- 撤销文件改动：git_restore_file，只处理用户明确指定的文件。");
        builder.AppendLine("- 提交代码：git_commit，提交前先检查 status/diff。");
        builder.AppendLine("- 验证：run_build、run_test。");
        builder.AppendLine("- 兜底：run_shell，仅当没有专用工具可用。");
        builder.AppendLine("- 计划：update_plan，用于创建和更新任务执行计划。");
        builder.AppendLine();
        builder.AppendLine("对于多步骤任务，先调用 update_plan 创建计划，再逐步执行。每完成一个步骤，调用 update_plan 更新状态。");
        builder.AppendLine();
        builder.AppendLine("自我评估：每轮工具调用后，评估任务是否已完成。如果目标已达成，直接总结结果，不再调用工具。不要做超出用户要求范围的事情。");
        builder.AppendLine();
        builder.AppendLine("当前项目：");
        builder.AppendLine($"- 名称：{Normalize(context.ProjectName, "AIChat")}");
        builder.AppendLine($"- 路径：{Normalize(context.ProjectPath, "(未设置)")}");
        builder.AppendLine($"- 执行模式：{Normalize(context.ExecutionMode, "Standard")}");
        if (!string.IsNullOrWhiteSpace(context.ProjectLoadSnapshot))
        {
            builder.AppendLine("- 加载快照：");
            foreach (var line in context.ProjectLoadSnapshot.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                builder.AppendLine($"  - {line}");
            }
        }
        if (!string.IsNullOrWhiteSpace(context.ProjectPreparationSummary))
        {
            builder.AppendLine($"- 启动准备：{context.ProjectPreparationSummary.Trim()}");
        }
        builder.AppendLine();

        // Inject AGENTS.md content if it exists
        if (!string.IsNullOrWhiteSpace(context.ProjectPath))
        {
            var agentsPath = Path.Combine(context.ProjectPath, "AGENTS.md");
            if (File.Exists(agentsPath))
            {
                try
                {
                    var agentsContent = File.ReadAllText(agentsPath);
                    if (!string.IsNullOrWhiteSpace(agentsContent))
                    {
                        builder.AppendLine("项目说明文件 (AGENTS.md)：");
                        builder.AppendLine(agentsContent.Trim());
                        builder.AppendLine();
                    }
                }
                catch
                {
                    // Non-fatal — continue without AGENTS.md
                }
            }
        }

        builder.AppendLine("当前启用工具：");

        if (context.EnabledToolIds.Count == 0)
        {
            builder.AppendLine("- 无。不能声称已经读取、修改或执行项目操作。");
            AppendInputArtifacts(builder, context.InputArtifactRefs);
            return builder.ToString().Trim();
        }

        foreach (var toolId in context.EnabledToolIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var mode = context.ToolPermissionModes.TryGetValue(toolId, out var configuredMode)
                ? configuredMode
                : ToolPermissionMode.ConfirmEachTime;
            builder.AppendLine($"- {toolId}：{DescribePermission(mode)}");
        }

        var contextPack = _contextPackBuilder.Build(
            context.FileIndex,
            context.WorkspaceSummary,
            context.PinnedContextItems);
        if (!string.IsNullOrWhiteSpace(contextPack))
        {
            builder.AppendLine();
            builder.AppendLine(contextPack);
        }

        AppendMemorySnippets(builder, context.MemorySnippets);
        AppendInputArtifacts(builder, context.InputArtifactRefs);
        AppendModelProfile(builder, context);

        // Per-provider prompt sections used to live here (DeepSeek
        // thinking guidance, etc.). After the 2026-08-02 catalog
        // prune, AIChat ships with MiniMax only and the catalog's
        // single ModelProfile carries the per-provider guidance
        // through AppendModelProfile above. No more branches here.

        return builder.ToString().Trim();
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string DescribePermission(ToolPermissionMode mode)
    {
        return mode switch
        {
            ToolPermissionMode.AutoReadOnly => "只读工具可自动执行",
            ToolPermissionMode.AllowForSession => "用户确认后可在本会话继续执行",
            ToolPermissionMode.Disabled => "已关闭，不要调用",
            _ => "每次执行前需要确认"
        };
    }

    private static void AppendInputArtifacts(StringBuilder builder, IReadOnlyList<string> inputArtifactRefs)
    {
        if (inputArtifactRefs.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("输入 artifact：");
        builder.AppendLine("- 这些引用是用户上传或粘贴输入的结构化摘要；需要更多细节时，按 ref 请求更完整内容。");
        foreach (var artifactRef in inputArtifactRefs.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {artifactRef.Trim()}");
        }
    }

    private static void AppendMemorySnippets(StringBuilder builder, IReadOnlyList<string> memorySnippets)
    {
        if (memorySnippets.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("相关长期记忆：");
        builder.AppendLine("- 这些记忆来自本项目历史对话和 Agent Run；把它们当作可复用线索，若与当前文件冲突，以实际文件为准。");
        foreach (var memory in memorySnippets.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
        {
            builder.AppendLine($"- {memory.Trim()}");
        }
    }

    private static void AppendModelProfile(StringBuilder builder, SystemPromptContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ModelProfileName))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("模型执行策略：");
        builder.AppendLine($"- Profile：{context.ModelProfileName}");
        AppendProfileLine(builder, "工具调用", context.ModelProfileToolCallPolicy);
        AppendProfileLine(builder, "思考策略", context.ModelProfileThinkingPolicy);
        AppendProfileLine(builder, "缓存策略", context.ModelProfileCacheStrategy);
        AppendProfileLine(builder, "提示建议", context.ModelProfilePromptGuidance);
    }

    private static void AppendProfileLine(StringBuilder builder, string title, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {title}：{value.Trim()}");
        }
    }
}
