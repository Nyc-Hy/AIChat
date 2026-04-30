using System.Text;
using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Prompting;

public sealed class SystemPromptBuilder
{
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
        builder.AppendLine();
        builder.AppendLine("工具优先级：");
        builder.AppendLine("- 理解项目：list_files、search_text、read_file。");
        builder.AppendLine("- 修改已有文件：apply_patch 优先，其次 edit_file；创建新文件才考虑 write_file。");
        builder.AppendLine("- 检查变更：git_status、git_diff。");
        builder.AppendLine("- 验证：run_build、run_test。");
        builder.AppendLine("- 兜底：run_shell，仅当没有专用工具可用。");
        builder.AppendLine();
        builder.AppendLine("当前项目：");
        builder.AppendLine($"- 名称：{Normalize(context.ProjectName, "AIChat")}");
        builder.AppendLine($"- 路径：{Normalize(context.ProjectPath, "(未设置)")}");
        builder.AppendLine();
        builder.AppendLine("当前启用工具：");

        if (context.EnabledToolIds.Count == 0)
        {
            builder.AppendLine("- 无。不能声称已经读取、修改或执行项目操作。");
            return builder.ToString().Trim();
        }

        foreach (var toolId in context.EnabledToolIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var mode = context.ToolPermissionModes.TryGetValue(toolId, out var configuredMode)
                ? configuredMode
                : ToolPermissionMode.ConfirmEachTime;
            builder.AppendLine($"- {toolId}：{DescribePermission(mode)}");
        }

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
}
