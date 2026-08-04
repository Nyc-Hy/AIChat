using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;
using Avalonia.Media;

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

    [Theory]
    [InlineData("Running", true, false, false, false, false, false)]
    [InlineData("Completed", false, true, false, false, false, false)]
    [InlineData("Failed", false, false, true, false, false, false)]
    [InlineData("Cancelled", false, false, false, false, true, false)]
    [InlineData("Skipped", false, false, false, false, false, true)]
    [InlineData("BudgetExceeded", false, false, false, true, false, false)]
    [InlineData("SomethingUnknown", false, false, false, false, false, false)]
    public void IsX_FlagsMatchStatus_ForXamlClassBinding(
        string status,
        bool isRunning,
        bool isCompleted,
        bool isFailed,
        bool isBudget,
        bool isCancelled,
        bool isSkipped)
    {
        // The XAML binds one Classes.X per IsX flag so the
        // sub-agent-status style can colour the duration text by
        // outcome. This test pins the mapping: exactly one IsX is
        // true per row, and it matches the Status the row was
        // constructed with. (The previous StatusKind string
        // property was dropped in commit e2c1db2 because the
        // IsX flags are what the XAML actually binds.)
        var run = NewRun(templateId: "explorer", task: "x");
        run.Status = status;
        var vm = new SubAgentRunViewModel(run);

        Assert.Equal(isRunning, vm.IsRunning);
        Assert.Equal(isCompleted, vm.IsCompleted);
        Assert.Equal(isFailed, vm.IsFailed);
        Assert.Equal(isBudget, vm.IsBudgetExceeded);
        Assert.Equal(isCancelled, vm.IsCancelled);
        Assert.Equal(isSkipped, vm.IsSkipped);
    }

    [Theory]
    [InlineData("Completed", "#5cd6a8")]
    [InlineData("Failed", "#ff6b6b")]
    [InlineData("Running", "#f5a623")]
    [InlineData("Cancelled", "#9aa0a6")]
    [InlineData("Skipped", "#9aa0a6")]
    [InlineData("BudgetExceeded", "#9aa0a6")]
    [InlineData("SomethingUnknown", "#9aa0a6")]
    public void StatusBrush_TracksStateOutcomeWithFixedPalette(
        string status, string expectedRgb)
    {
        // The per-row status dot in the Environment panel uses
        // StatusBrush (a small fixed palette: green / red / amber /
        // grey) so the user can scan the panel and read state from
        // colour, not from reading the label. Pin the mapping so
        // a future tweak of the palette doesn't drift without a
        // failing test. SolidColorBrush's Color serialises with an
        // explicit alpha prefix; we compare the RGB half (drop the
        // leading "ff") so the test stays focused on the palette
        // value rather than the alpha channel (which is always
        // fully-opaque for these dots).
        var run = NewRun(templateId: "explorer", task: "x");
        run.Status = status;
        var vm = new SubAgentRunViewModel(run);

        var brush = (SolidColorBrush)vm.StatusBrush;
        var actualHex = brush.Color.ToString().ToLowerInvariant();
        // "#ffaabbcc" → "#aabbcc" (drop leading alpha).
        Assert.Equal("#" + actualHex[(actualHex.Length - 6)..], expectedRgb);
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

    // 1.0.1: DurationDisplay is normally
    // only set on Update() (when the
    // harness emits status=running /
    // completed). For a long-running
    // sub-agent (30+ seconds) the user
    // would otherwise stare at
    // "运行中…" the whole time with
    // no progress signal.
    // EnvironmentPanelViewModel runs a
    // 1Hz DispatcherTimer that calls
    // RefreshRunningDuration on every
    // running row; these tests pin the
    // formatter so the running and
    // completed paths stay in lockstep.
    [Fact]
    public void RefreshRunningDuration_ReplacesStaticRunningWithElapsed()
    {
        // Pick a StartedAt in the past
        // so the elapsed time is a real
        // "12s"-class number, not
        // "<1s" (which the formatter
        // would also report for a row
        // that was just started this
        // tick).
        var startedAt = DateTimeOffset.Now.AddSeconds(-12);
        var run = NewRun("explorer", "find foo", startedAt: startedAt);
        var vm = new SubAgentRunViewModel(run);
        // Default Ctor paths through
        // FormatDuration with null
        // completedAt → "运行中…".
        Assert.Equal("运行中…", vm.DurationDisplay);

        vm.RefreshRunningDuration();

        // After refresh, the row shows
        // a real elapsed-time string
        // (12s ± 1 tick). The exact
        // number drifts per tick so
        // match the format rather than
        // the literal value.
        Assert.NotEqual("运行中…", vm.DurationDisplay);
        Assert.EndsWith("s", vm.DurationDisplay);
    }

    [Fact]
    public void FormatDuration_CompletedRow_StaysFinal()
    {
        // The completed-row path goes
        // through FormatDuration, not
        // RefreshRunningDuration, so
        // it's untouched by the 1Hz
        // timer. The formatter returns
        // the same shape
        // RefreshRunningDuration would
        // for a stopped clock, which
        // means a row that's been
        // running for "1m 5s" and then
        // completes will read "1m 5s"
        // both before and after the
        // status flip — no jarring
        // change in the duration slot.
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(-5);
        var completedAt = startedAt.AddSeconds(65);
        var run = new AgentSubAgentRun
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentRunId = "parent",
            TemplateId = "explorer",
            Task = "find bar",
            Status = "Completed",
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
        var vm = new SubAgentRunViewModel(run);
        Assert.Equal("1m 5s", vm.DurationDisplay);

        // A subsequent
        // RefreshRunningDuration call
        // on a Completed row would
        // also produce a sensible
        // string (the formatter
        // doesn't care about Status —
        // only about StartedAt). The
        // 1Hz timer in the panel
        // already filters non-running
        // rows out, so this case
        // shouldn't happen in
        // practice, but the formatter
        // is robust to it.
        vm.RefreshRunningDuration();
        // The exact text drifts
        // because the live-tick path
        // uses Now instead of
        // CompletedAt, so a 1-second
        // drift is expected. The
        // "1m" minute marker is the
        // stable part of the format.
        Assert.StartsWith("1m", vm.DurationDisplay);
    }

    // ---- 1.0.1: per-row inline expand for the Summary text ----

    [Fact]
    public void IsExpanded_DefaultsFalse_ToggleFlips()
    {
        // Each row starts collapsed
        // (the row's task description
        // is usually enough to know
        // what the sub-agent did).
        // Toggle flips state, like
        // every other inline-expand
        // surface in the app (Plan /
        // Sources).
        var run = NewRun("explorer", "find foo", summary: "ok");
        var vm = new SubAgentRunViewModel(run);
        Assert.False(vm.IsExpanded);

        vm.ToggleExpand();
        Assert.True(vm.IsExpanded);

        vm.ToggleExpand();
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void HasSummary_TrueForNonEmptySummary()
    {
        // The expand panel only
        // renders for rows that have
        // something to show. Empty
        // / whitespace summaries
        // don't count — the
        // "expand this empty box"
        // UX is the kind of paper
        // cut daily drivers notice.
        var withSummary = NewRun("explorer", "x", summary: "did the thing");
        var emptySummary = NewRun("explorer", "x", summary: "");
        var whitespaceSummary = NewRun("explorer", "x", summary: "   ");

        Assert.True(new SubAgentRunViewModel(withSummary).HasSummary);
        Assert.False(new SubAgentRunViewModel(emptySummary).HasSummary);
        Assert.False(new SubAgentRunViewModel(whitespaceSummary).HasSummary);
    }

    [Fact]
    public void ShouldShowSummary_RequiresBothIsExpandedAndHasSummary()
    {
        // The XAML binds the expand
        // panel's IsVisible to
        // ShouldShowSummary, not just
        // IsExpanded. A row with no
        // summary but IsExpanded=true
        // would otherwise render an
        // empty bordered block (same
        // fix as the Plan detail
        // panel — commit aeedf40).
        var runWithSummary = NewRun("explorer", "x", summary: "found 3 files");
        var runEmptySummary = NewRun("explorer", "x", summary: "");

        var expanded = new SubAgentRunViewModel(runWithSummary);
        expanded.IsExpanded = true;
        Assert.True(expanded.ShouldShowSummary);

        var collapsed = new SubAgentRunViewModel(runWithSummary);
        Assert.False(collapsed.ShouldShowSummary);

        var expandedButEmpty = new SubAgentRunViewModel(runEmptySummary);
        expandedButEmpty.IsExpanded = true;
        Assert.False(expandedButEmpty.ShouldShowSummary);
    }

    [Fact]
    public void ShouldShowSummary_FlipsWhenSummaryArrivesMidSession()
    {
        // The harness can deliver a
        // Summary after the row is
        // already expanded (a long
        // sub-agent that finishes
        // while the user has the
        // row open). ShouldShowSummary
        // must re-raise on Summary
        // change so the panel
        // transitions from "expanded
        // but empty" to "expanded
        // with content" without a
        // round-trip of stale UI.
        var run = NewRun("explorer", "long", summary: "");
        var vm = new SubAgentRunViewModel(run);
        vm.IsExpanded = true;
        Assert.False(vm.ShouldShowSummary);

        run.Summary = "finally finished";
        vm.Update(run);

        Assert.True(vm.HasSummary);
        Assert.True(vm.ShouldShowSummary);
    }
}
