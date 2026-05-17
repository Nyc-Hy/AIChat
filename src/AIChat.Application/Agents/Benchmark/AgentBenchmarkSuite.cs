namespace AIChat.Application.Agents.Benchmark;

public static class AgentBenchmarkSuite
{
    public static IReadOnlyList<AgentBenchmarkTask> DefaultTasks { get; } =
    [
        new("readonly-analysis", "只读项目分析", "analysis", "分析项目结构并指出下一步", MaxToolCalls: 6, MaxEstimatedPromptTokens: 5000),
        new("small-bugfix", "小型缺陷修复", "bugfix", "修复一个局部 bug 并验证", RequiresMutation: true, RequiresVerification: true, MaxToolCalls: 12, MaxEstimatedPromptTokens: 8000),
        new("test-repair", "测试失败修复", "verification", "根据失败测试修复代码", RequiresMutation: true, RequiresVerification: true, MaxToolCalls: 16, MaxEstimatedPromptTokens: 10000),
        new("docs-update", "文档更新", "docs", "更新项目文档", RequiresMutation: true, MaxToolCalls: 8, MaxEstimatedPromptTokens: 6000),
        new("plugin-mcp", "插件和 MCP 使用", "plugins", "发现并调用插件或 MCP 工具", MaxToolCalls: 10, MaxEstimatedPromptTokens: 7000)
    ];
}
