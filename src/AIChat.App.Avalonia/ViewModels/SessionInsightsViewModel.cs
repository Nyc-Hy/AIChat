using System.Collections.ObjectModel;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the right-rail "session insights" surface: the per-goal context
// preview and the live session-metrics strip. PR-5 scope: pure extraction
// from MainWindowViewModel.
//
// The view-model owns all session-related counters (last token counts,
// tool rounds, runtime, current run start time) because the parent has
// no other use for them. Updates flow in through explicit methods — no
// events — because the parent is the only writer.
public sealed partial class SessionInsightsViewModel : ViewModelBase
{
    private const int SystemAndToolSchemaBudget = 1500;
    private const int MaxIncludedFiles = 500;
    private const int ContextBudgetTokens = 900;

    private int _lastContextEstimatedTokens;
    private int _lastInputEstimatedTokens;
    private int _lastOutputEstimatedTokens;
    private int _lastToolRounds;
    private int _lastModelCalls;
    private DateTimeOffset? _currentRunStartedAt;
    private string _lastRuntime = "—";
    private string _lastVerification = "—";

    public ObservableCollection<ContextPreviewItemViewModel> ContextPreview { get; } = [];
    public ObservableCollection<SessionMetricViewModel> SessionMetrics { get; } = [];

    public SessionInsightsViewModel()
    {
        SeedEmptyState();
    }

    // First-load default: empty preview + "—" metrics. The parent calls
    // this from its own SeedEmptyState to keep the visible state coherent
    // before any real data has loaded.
    public void SeedEmptyState()
    {
        ContextPreview.Clear();
        ContextPreview.Add(new ContextPreviewItemViewModel("项目规则", "AGENTS.md 与稳定项目说明"));
        ContextPreview.Add(new ContextPreviewItemViewModel("相关文件", "输入任务后自动选择"));
        RefreshMetrics(
            contextTokens: 0,
            inputTokens: 0,
            outputTokens: 0,
            cacheHit: "未知",
            toolRounds: 0,
            modelCalls: 0,
            runtime: "—",
            verification: "—");
    }

    // Rebuilds the context preview for the given goal against the supplied
    // project (may be null) and updates the session metrics to reflect the
    // new estimated context + input token counts.
    public void PrepareContextPreview(string goal, ProjectWorkspace? project, bool noWriteMode)
    {
        ContextPreview.Clear();
        if (project is null || string.IsNullOrWhiteSpace(project.Path) || !Directory.Exists(project.Path))
        {
            ContextPreview.Add(new ContextPreviewItemViewModel("项目", "运行任务前请选择一个代码仓库。"));
            ContextPreview.Add(new ContextPreviewItemViewModel("安全", noWriteMode ? "只读模式已开启。" : "风险操作会请求确认。"));
            RefreshMetrics(
                contextTokens: 0,
                inputTokens: 0,
                outputTokens: _lastOutputEstimatedTokens,
                cacheHit: "未知",
                toolRounds: _lastToolRounds,
                modelCalls: _lastModelCalls,
                runtime: _lastRuntime,
                verification: _lastVerification);
            return;
        }

        var resolvedGoal = string.IsNullOrWhiteSpace(goal) ? "项目概览" : goal.Trim();
        var fileIndex = new ProjectFileIndexBuilder().Build(project.Path, maxFiles: MaxIncludedFiles);
        var contextPack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = resolvedGoal,
            Phase = AgentRunPhase.GatheringContext,
            FileIndex = fileIndex,
            PinnedItems = project.PinnedContext,
            InputArtifacts = project.InputArtifacts,
            MemorySnippets = project.Memories.Select(memory => memory.Content).ToList(),
            MaxTokens = ContextBudgetTokens
        });

        ContextPreview.Add(new ContextPreviewItemViewModel(
            "规则",
            File.Exists(Path.Combine(project.Path, "AGENTS.md")) ? "已找到 AGENTS.md" : "未找到 AGENTS.md"));
        ContextPreview.Add(new ContextPreviewItemViewModel(
            "文件",
            $"纳入 {contextPack.IncludedFiles.Count} 个，略过 {contextPack.OmittedButRelevantRefs.Count} 个"));
        ContextPreview.Add(new ContextPreviewItemViewModel(
            "记忆",
            $"已接受 {project.Memories.Count} 条"));
        ContextPreview.Add(new ContextPreviewItemViewModel(
            "检查",
            project.VerificationCommands.Count == 0 ? "未检测到命令" : $"{project.VerificationCommands.Count} 条命令"));
        ContextPreview.Add(new ContextPreviewItemViewModel(
            "预算",
            $"约 {contextPack.EstimatedTokens} tokens"));

        _lastContextEstimatedTokens = contextPack.EstimatedTokens;
        _lastInputEstimatedTokens = EstimateInputTokens(contextPack.EstimatedTokens, resolvedGoal);
        RefreshMetrics(
            contextTokens: _lastContextEstimatedTokens,
            inputTokens: _lastInputEstimatedTokens,
            outputTokens: _lastOutputEstimatedTokens,
            cacheHit: "估算",
            toolRounds: _lastToolRounds,
            modelCalls: _lastModelCalls,
            runtime: _lastRuntime,
            verification: _lastVerification);
    }

    // Marks the start of a new agent run. Captures the start time so the
    // runtime can be displayed even before the AgentRun is materialised.
    public void BeginRun(string goal, int contextTokens, int verificationCommandCount)
    {
        _currentRunStartedAt = DateTimeOffset.Now;
        _lastContextEstimatedTokens = contextTokens;
        _lastInputEstimatedTokens = EstimateInputTokens(contextTokens, goal);
        _lastOutputEstimatedTokens = 0;
        _lastToolRounds = 0;
        _lastModelCalls = 0;
        _lastRuntime = "运行中";
        _lastVerification = verificationCommandCount == 0 ? "未配置" : "待运行";
        RefreshMetrics(
            _lastContextEstimatedTokens,
            _lastInputEstimatedTokens,
            _lastOutputEstimatedTokens,
            "未知",
            _lastToolRounds,
            _lastModelCalls,
            _lastRuntime,
            _lastVerification);
    }

    // Called whenever the agent harness reports a new event so the metric
    // strip stays in sync. When the event carries a populated AgentRun we
    // pull authoritative counts from it; otherwise we fall back to the
    // captured start time.
    public void UpdateMetrics(AgentRun? run, string assistantText, int verificationCommandCount)
    {
        _lastOutputEstimatedTokens = EstimateTextTokens(assistantText);
        if (run is not null)
        {
            _lastContextEstimatedTokens = run.ContextEstimatedTokens;
            _lastToolRounds = run.ToolCallCount;
            _lastModelCalls = run.ModelCallCount;
            _lastRuntime = FormatRuntime(run.StartedAt, run.CompletedAt);
            _lastVerification = run.Verifications.Count == 0
                ? (verificationCommandCount > 0 ? "未运行" : "未配置")
                : $"{run.Verifications.Count(verification => verification.IsSuccess)}/{run.Verifications.Count} 通过";
        }
        else if (_currentRunStartedAt is not null)
        {
            _lastRuntime = FormatRuntime(_currentRunStartedAt.Value, null);
        }

        RefreshMetrics(
            _lastContextEstimatedTokens,
            _lastInputEstimatedTokens,
            _lastOutputEstimatedTokens,
            "未知",
            _lastToolRounds,
            _lastModelCalls,
            _lastRuntime,
            _lastVerification);
    }

    private void RefreshMetrics(
        int contextTokens,
        int inputTokens,
        int outputTokens,
        string cacheHit,
        int toolRounds,
        int modelCalls,
        string runtime,
        string verification)
    {
        SessionMetrics.Clear();
        SessionMetrics.Add(new SessionMetricViewModel(
            "上下文",
            FormatTokenCount(contextTokens),
            "本次任务选入上下文的估算 tokens，来自 ContextRouter。发送前可用于判断上下文是否过大。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "输入",
            FormatTokenCount(inputTokens),
            "估算输入 tokens，包含上下文、用户目标和系统提示的粗略预算。Provider 精确用量接入后会替换此估算。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "输出",
            outputTokens > 0 ? FormatTokenCount(outputTokens) : "—",
            "估算输出 tokens，按当前 assistant 文本长度估算。Provider 精确输出 tokens 尚未统一接入。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "缓存",
            cacheHit,
            "Provider 返回缓存命中数据时显示精确值；当前未返回时显示未知或缓存友好估算。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "工具轮次",
            $"{toolRounds}",
            $"已执行工具调用次数。模型调用次数：{modelCalls}。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "耗时",
            runtime,
            "从本轮任务开始到当前时刻的耗时；运行中显示\"运行中\"，完成后显示具体时长。"));
        SessionMetrics.Add(new SessionMetricViewModel(
            "检查",
            verification,
            "本轮任务的验证结果。运行中显示\"待运行\"，完成时显示通过/总条数；未配置验证命令时显示\"未配置\"。"));
    }

    private static int EstimateInputTokens(int contextTokens, string prompt)
    {
        return Math.Max(0, contextTokens) + EstimateTextTokens(prompt) + SystemAndToolSchemaBudget;
    }

    private static int EstimateTextTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Rough heuristic: ~1 token per 1.5 characters of mixed CJK/Latin
        // text. Matches the original MainWindowViewModel implementation
        // so behaviour is identical after the extraction.
        return Math.Max(1, (int)Math.Round(text.Length / 1.5));
    }

    private static string FormatTokenCount(int tokens) => tokens switch
    {
        <= 0 => "—",
        < 1_000 => tokens.ToString(),
        < 1_000_000 => $"{tokens / 1000.0:0.#}K",
        _ => $"{tokens / 1_000_000.0:0.##}M"
    };

    private static string FormatRuntime(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        var end = completedAt ?? DateTimeOffset.Now;
        var elapsed = end - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds:0.#}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
    }
}
