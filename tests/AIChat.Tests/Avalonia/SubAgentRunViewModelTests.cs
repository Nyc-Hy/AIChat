using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Avalonia;

// The plan panel surfaces a live "子 Agent" sub-section whenever the
// harness dispatches a sub-agent. Each row is a SubAgentRunViewModel
// wrapping the persisted AgentSubAgentRun. These tests cover the
// pure VM transformation logic (template name formatting, duration
// formatting, status flag flips on update) — the live UI events are
// exercised in the smoke / launch tests.
public class SubAgentRunViewModelTests
{
    [Fact]
    public void Ctor_WithExplorerTemplate_FormatsDisplayName()
    {
        var run = NewRun(templateId: "explorer", task: "扫描项目结构");

        var vm = new SubAgentRunViewModel(run);

        Assert.Equal("Explorer", vm.TemplateDisplay);
        Assert.Equal("扫描项目结构", vm.Task);
        Assert.True(vm.IsRunning);
        Assert.False(vm.IsCompleted);
        Assert.Equal("运行中…", vm.DurationDisplay);
    }

    [Fact]
    public void Ctor_WithUnknownTemplate_KeepsRawId()
    {
        var run = NewRun(templateId: "reviewer", task: "lint 校验");

        var vm = new SubAgentRunViewModel(run);

        Assert.Equal("reviewer", vm.TemplateDisplay);
    }

    [Fact]
    public void Ctor_WithEmptyTemplateId_FallsBackToDefault()
    {
        var run = NewRun(templateId: "", task: "noop");

        var vm = new SubAgentRunViewModel(run);

        Assert.Equal("Sub-agent", vm.TemplateDisplay);
    }

    [Fact]
    public void Ctor_WithCompletedRun_SetsDurationFromStartedToCompleted()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-42);
        var run = NewRun(templateId: "explorer", task: "查 git 状态", startedAt: started);
        run.Status = "Completed";
        run.CompletedAt = started.AddSeconds(42);

        var vm = new SubAgentRunViewModel(run);

        Assert.True(vm.IsCompleted);
        Assert.False(vm.IsRunning);
        Assert.Equal("42s", vm.DurationDisplay);
    }

    [Fact]
    public void Ctor_WithSubSecondCompletion_FormatsAsLessThanOneSecond()
    {
        var started = DateTimeOffset.UtcNow;
        var run = NewRun(templateId: "explorer", task: "快速任务", startedAt: started);
        run.Status = "Completed";
        run.CompletedAt = started.AddMilliseconds(200);

        var vm = new SubAgentRunViewModel(run);

        Assert.Equal("<1s", vm.DurationDisplay);
    }

    [Fact]
    public void Update_FromRunningToCompleted_FlipsStatusAndRecomputesDuration()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-7);
        var run = NewRun(templateId: "explorer", task: "读取 README", startedAt: started);
        var vm = new SubAgentRunViewModel(run);

        Assert.True(vm.IsRunning);

        run.Status = "Completed";
        run.CompletedAt = started.AddSeconds(7);
        run.Summary = "已确认文档结构";
        run.ToolCallCount = 3;
        vm.Update(run);

        Assert.True(vm.IsCompleted);
        Assert.False(vm.IsRunning);
        Assert.Equal("7s", vm.DurationDisplay);
        Assert.Equal("已确认文档结构", vm.Summary);
        Assert.Equal(3, vm.ToolCallCount);
    }

    [Fact]
    public void Update_WithEmptySummary_NullsOutDisplay()
    {
        var run = NewRun(templateId: "explorer", task: "noop", summary: "ok");
        var vm = new SubAgentRunViewModel(run);
        Assert.Equal("ok", vm.Summary);

        run.Summary = "";
        vm.Update(run);

        Assert.Null(vm.Summary);
    }

    [Fact]
    public void Update_PreservesStartedAtFromOriginalRun()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var run = NewRun(templateId: "explorer", task: "long", startedAt: started);
        run.CompletedAt = started.AddSeconds(45);
        var vm = new SubAgentRunViewModel(run);
        // 45s stays in the sub-minute "<Ns>" format.
        Assert.Equal("45s", vm.DurationDisplay);

        // Update the underlying run with a new CompletedAt — duration should
        // recompute against the ORIGINAL StartedAt, not the just-updated one.
        run.CompletedAt = started.AddSeconds(120);
        vm.Update(run);
        Assert.Equal("2m 0s", vm.DurationDisplay);
    }

    private static AgentSubAgentRun NewRun(string templateId, string task, DateTimeOffset? startedAt = null, string summary = "")
    {
        return new AgentSubAgentRun
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentRunId = "parent",
            TemplateId = templateId,
            Task = task,
            Status = "Running",
            Summary = summary,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
