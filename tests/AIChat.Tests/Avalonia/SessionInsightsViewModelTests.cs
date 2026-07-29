using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Avalonia;

// Unit tests for PR-5: the right-rail "session insights" view-model
// (context preview + live metrics). Only touches pure CLR types, so
// no headless platform required.
public class SessionInsightsViewModelTests : IDisposable
{
    private readonly string _tempRoot;

    public SessionInsightsViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AIChatInsightsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Constructor_SeedsEmptyPreviewAndZeroMetrics()
    {
        var vm = new SessionInsightsViewModel();

        Assert.Equal(2, vm.ContextPreview.Count);
        Assert.Equal("项目规则", vm.ContextPreview[0].Label);
        Assert.Equal(7, vm.SessionMetrics.Count);
        Assert.Equal("上下文", vm.SessionMetrics[0].Label);
        Assert.Equal("—", vm.SessionMetrics[0].Value);
    }

    [Fact]
    public void PrepareContextPreview_WithNullProject_ShowsProjectMissingAndSafetyNotes()
    {
        var vm = new SessionInsightsViewModel();

        vm.PrepareContextPreview("fix login bug", project: null, noWriteMode: true);

        Assert.Equal(2, vm.ContextPreview.Count);
        Assert.Equal("项目", vm.ContextPreview[0].Label);
        Assert.Equal("安全", vm.ContextPreview[1].Label);
        Assert.Equal("只读模式已开启。", vm.ContextPreview[1].Detail);
    }

    [Fact]
    public void PrepareContextPreview_WithValidProject_PopulatesAllRows()
    {
        var project = new ProjectWorkspace
        {
            Id = "p1",
            Name = "Sample",
            Path = _tempRoot
        };
        var vm = new SessionInsightsViewModel();

        vm.PrepareContextPreview("fix login bug", project, noWriteMode: false);

        Assert.Equal(5, vm.ContextPreview.Count);
        Assert.Equal("规则", vm.ContextPreview[0].Label);
        Assert.Equal("未找到 AGENTS.md", vm.ContextPreview[0].Detail); // no AGENTS.md in temp dir
        Assert.Equal("文件", vm.ContextPreview[1].Label);
        Assert.Equal("记忆", vm.ContextPreview[2].Label);
        Assert.Equal("检查", vm.ContextPreview[3].Label);
        Assert.Equal("预算", vm.ContextPreview[4].Label);
    }

    [Fact]
    public void PrepareContextPreview_UpdatesContextTokenMetrics()
    {
        // With a real (non-empty) project the context preview includes a
        // "预算" row that reports the router's token estimate. We assert
        // the row exists; the exact number is the router's responsibility.
        var project = new ProjectWorkspace { Id = "p1", Name = "Sample", Path = _tempRoot };
        var vm = new SessionInsightsViewModel();

        vm.PrepareContextPreview("analyse the project", project, noWriteMode: false);

        Assert.Contains(vm.ContextPreview, item => item.Label == "预算");
    }

    [Fact]
    public void BeginRun_SetsRunningStateAndResetsCounters()
    {
        var vm = new SessionInsightsViewModel();
        vm.PrepareContextPreview("anything", project: null, noWriteMode: false);

        vm.BeginRun(goal: "fix it", contextTokens: 1500, verificationCommandCount: 2);

        Assert.Equal("运行中", vm.SessionMetrics.First(m => m.Label == "耗时").Value);
        Assert.Equal("待运行", vm.SessionMetrics.First(m => m.Label == "检查").Value);
        Assert.Equal("0", vm.SessionMetrics.First(m => m.Label == "工具轮次").Value);
        Assert.Equal("—", vm.SessionMetrics.First(m => m.Label == "输出").Value);
    }

    [Fact]
    public void UpdateMetrics_WithoutRun_KeepsStartedAtAndUpdatesRuntime()
    {
        var vm = new SessionInsightsViewModel();
        vm.BeginRun("fix it", contextTokens: 1000, verificationCommandCount: 1);
        // Synthesise a tiny delay so the runtime string is stable.
        Thread.Sleep(50);

        vm.UpdateMetrics(run: null, assistantText: "thinking...", verificationCommandCount: 1);

        var runtime = vm.SessionMetrics.First(m => m.Label == "耗时").Value;
        Assert.NotEqual("运行中", runtime); // should have advanced
    }

    [Fact]
    public void UpdateMetrics_WithRun_ReflectsRunCounts()
    {
        var vm = new SessionInsightsViewModel();
        vm.BeginRun("fix it", contextTokens: 1000, verificationCommandCount: 1);
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = "c1",
            ContextEstimatedTokens = 999,
            ToolCallCount = 3,
            ModelCallCount = 2,
            StartedAt = DateTimeOffset.Now.AddSeconds(-2),
            CompletedAt = DateTimeOffset.Now,
            Verifications = { new AgentVerification { IsSuccess = true }, new AgentVerification { IsSuccess = false } }
        };

        vm.UpdateMetrics(run, assistantText: "hello world", verificationCommandCount: 2);

        Assert.Equal("999", vm.SessionMetrics.First(m => m.Label == "上下文").Value);
        Assert.Equal("3", vm.SessionMetrics.First(m => m.Label == "工具轮次").Value);
        Assert.Equal("1/2 通过", vm.SessionMetrics.First(m => m.Label == "检查").Value);
    }

    [Fact]
    public void UpdateMetrics_WithRunNoVerificationsAndNoCommands_ShowsNotConfigured()
    {
        var vm = new SessionInsightsViewModel();
        vm.BeginRun("fix it", contextTokens: 100, verificationCommandCount: 0);
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = "c1",
            ContextEstimatedTokens = 100,
            ToolCallCount = 0,
            ModelCallCount = 1,
            StartedAt = DateTimeOffset.Now,
            CompletedAt = DateTimeOffset.Now
        };

        vm.UpdateMetrics(run, assistantText: "ok", verificationCommandCount: 0);

        Assert.Equal("未配置", vm.SessionMetrics.First(m => m.Label == "检查").Value);
    }
}
