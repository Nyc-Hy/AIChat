using System.Text;
using AIChat.Application.Workspace;
using AIChat.Domain.Context;

namespace AIChat.Application.Context;

public sealed class ProjectContextPackBuilder
{
    // Hard cap in estimated tokens (chars / 3.6).
    private const int MaxEstimatedTokens = 2000;

    public string Build(ProjectFileIndex? fileIndex, string workspaceSummary, IReadOnlyList<PinnedContextItem> pinnedItems)
    {
        var builder = new StringBuilder();

        if (fileIndex is not null && fileIndex.Entries.Count > 0)
        {
            AppendFileIndex(builder, fileIndex);
        }

        if (!string.IsNullOrWhiteSpace(workspaceSummary))
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine("## 工作区状态");
            builder.AppendLine(workspaceSummary);
        }

        if (pinnedItems.Count > 0)
        {
            if (builder.Length > 0) builder.AppendLine();
            AppendPinnedContext(builder, pinnedItems);
        }

        // Trim if over budget
        var result = builder.ToString().Trim();
        var estimatedTokens = result.Length / 3.6;
        if (estimatedTokens > MaxEstimatedTokens)
        {
            result = TrimToBudget(result, MaxEstimatedTokens);
        }

        return result;
    }

    private static void AppendFileIndex(StringBuilder builder, ProjectFileIndex index)
    {
        builder.AppendLine("## 项目文件索引");
        builder.AppendLine($"根目录：{index.RootPath}");
        builder.AppendLine($"文件总数：{index.Entries.Count}");
        if (index.WasTruncated)
        {
            builder.AppendLine("（文件列表被截断）");
        }

        var groups = index.Entries
            .GroupBy(e => e.TypeTag)
            .OrderBy(g => GetGroupPriority(g.Key));

        foreach (var group in groups)
        {
            builder.AppendLine();
            builder.AppendLine($"### {GetGroupLabel(group.Key)} ({group.Count()})");
            foreach (var entry in group)
            {
                builder.AppendLine($"- {entry.RelativePath}");
            }
        }
    }

    private static void AppendPinnedContext(StringBuilder builder, IReadOnlyList<PinnedContextItem> items)
    {
        builder.AppendLine("## 固定上下文");
        foreach (var item in items)
        {
            var lineRange = item.StartLine > 0 && item.EndLine > 0
                ? $" (行 {item.StartLine}-{item.EndLine})"
                : "";
            var note = string.IsNullOrWhiteSpace(item.Note) ? "" : $" — {item.Note}";
            builder.AppendLine($"- {item.Path}{lineRange}{note}");
        }
    }

    private static string TrimToBudget(string text, int maxTokens)
    {
        // Progressive trimming: remove asset group, then doc group, then config group
        var priorities = new[] { "asset", "doc", "config" };
        var result = text;

        foreach (var priority in priorities)
        {
            if (result.Length / 3.6 <= maxTokens)
            {
                return result;
            }

            result = RemoveGroup(result, priority);
        }

        // If still over budget, truncate
        var maxChars = (int)(maxTokens * 3.6);
        return result.Length > maxChars ? result[..maxChars] + "\n..." : result;
    }

    private static string RemoveGroup(string text, string typeTag)
    {
        var label = GetGroupLabel(typeTag);
        var startMarker = $"### {label} (";
        var normalized = text.Replace("\r\n", "\n");
        var startIndex = normalized.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return text;
        }

        // Find the end of this group (next ### or end of text)
        var endIndex = normalized.IndexOf("\n### ", startIndex + startMarker.Length, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            // This is the last group, remove from startIndex to end
            return normalized[..startIndex].TrimEnd();
        }

        return (normalized[..startIndex] + normalized[endIndex..]).TrimEnd();
    }

    private static string GetGroupLabel(string typeTag)
    {
        return typeTag switch
        {
            "source" => "源代码",
            "test" => "测试",
            "config" => "配置",
            "doc" => "文档",
            _ => "其他"
        };
    }

    private static int GetGroupPriority(string typeTag)
    {
        return typeTag switch
        {
            "source" => 0,
            "test" => 1,
            "config" => 2,
            "doc" => 3,
            _ => 4
        };
    }
}
