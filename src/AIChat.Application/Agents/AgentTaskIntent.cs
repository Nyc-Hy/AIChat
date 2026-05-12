using System.Text.RegularExpressions;

namespace AIChat.Application.Agents;

public static class AgentTaskIntent
{
    private static readonly string[] MutationWords =
    [
        "创建", "新建", "生成", "实现", "写一个", "做一个", "加一个", "新增",
        "修改", "改成", "改为", "替换", "删除", "修复", "优化", "重构",
        "create", "implement", "write", "modify", "change", "replace", "fix", "update", "add"
    ];

    private static readonly Regex ChineseNegatedMutationClause = new(
        "(不要|别|无需|不需要|不用|禁止|避免)[^。！？；;\\r\\n]*(创建|新建|生成|实现|写|新增|修改|更改|改动|改写|编辑|替换|删除|修复|优化|重构|提交|改)[^。！？；;\\r\\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EnglishNegatedMutationClause = new(
        "\\b(do not|don't|dont|without|no need to|need not|never|avoid)\\b[^.!?;\\r\\n]*(create|implement|write|modify|change|edit|replace|delete|fix|update|add|commit)[^.!?;\\r\\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool RequiresProjectMutation(string goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return false;
        }

        var normalized = RemoveNegatedMutationClauses(goal);
        return MutationWords.Any(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string RemoveNegatedMutationClauses(string goal)
    {
        var normalized = ChineseNegatedMutationClause.Replace(goal, " ");
        return EnglishNegatedMutationClause.Replace(normalized, " ");
    }
}
