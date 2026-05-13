using System.Text.RegularExpressions;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class AgentCompletionEvidenceChecker
{
    private static readonly Regex NegatedMutationClaim = new(
        "(没有|未|不曾|尚未|无需|不需要|不要|no|not|without)[^。！？.!?;\\r\\n]*(修改|创建|新建|新增|删除|提交|写入|改动|modified|created|added|deleted|committed|wrote|changed|updated)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MutationClaim = new(
        "(已|已经|完成|我(已经)?|本轮|this run|i have|i've|done)[^。！？.!?;\\r\\n]*(修改|创建|新建|新增|删除|提交|写入|改动|modified|created|added|deleted|committed|wrote|changed|updated)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NegatedVerificationClaim = new(
        "(没有|未|不曾|尚未|无需|不需要|不要|no|not|without)[^。！？.!?;\\r\\n]*(测试|构建|验证|test|tests|build|verified|verification)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex VerificationClaim = new(
        "(已|已经|完成|通过|运行|执行|我(已经)?|this run|i have|i've|ran|run|passed)[^。！？.!?;\\r\\n]*(测试|构建|验证|test|tests|build|verified|verification)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public AgentCompletionEvidenceReport Check(string assistantContent, AgentRun run)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return AgentCompletionEvidenceReport.NoClaims();
        }

        var normalized = assistantContent.ReplaceLineEndings(" ");
        var mutationClaim = HasClaim(normalized, MutationClaim, NegatedMutationClaim);
        var verificationClaim = HasClaim(normalized, VerificationClaim, NegatedVerificationClaim);
        var risks = new List<string>();

        if (mutationClaim && !run.MutationToolSucceeded)
        {
            risks.Add("最终回复声称已修改/创建/删除/提交文件，但本轮没有成功的写入或提交工具记录。");
        }

        if (verificationClaim && !run.Verifications.Any(verification => verification.IsSuccess))
        {
            risks.Add("最终回复声称已运行测试/构建/验证，但本轮没有成功的验证工具记录。");
        }

        if (risks.Count == 0)
        {
            return mutationClaim || verificationClaim
                ? AgentCompletionEvidenceReport.ClaimsSatisfied()
                : AgentCompletionEvidenceReport.NoClaims();
        }

        return new AgentCompletionEvidenceReport(risks);
    }

    private static bool HasClaim(string content, Regex positive, Regex negative)
    {
        var withoutNegatedClaims = negative.Replace(content, " ");
        return positive.IsMatch(withoutNegatedClaims);
    }
}

public sealed class AgentCompletionEvidenceReport
{
    public AgentCompletionEvidenceReport(IReadOnlyList<string> risks)
    {
        Risks = risks;
    }

    public IReadOnlyList<string> Risks { get; }
    public bool HasRisk => Risks.Count > 0;
    public bool HasClaims => !string.Equals(Summary, NoClaimSummary, StringComparison.Ordinal);
    public string Summary => HasRisk
        ? "结果一致性：存在风险"
        : _summary;

    private string _summary { get; init; } = NoClaimSummary;

    private const string NoClaimSummary = "结果一致性：未检测到需校验的修改或验证声明";

    public static AgentCompletionEvidenceReport NoClaims()
    {
        return new AgentCompletionEvidenceReport([])
        {
            _summary = NoClaimSummary
        };
    }

    public static AgentCompletionEvidenceReport ClaimsSatisfied()
    {
        return new AgentCompletionEvidenceReport([])
        {
            _summary = "结果一致性：声明与工具记录一致"
        };
    }
}
