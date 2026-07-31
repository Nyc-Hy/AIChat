using AIChat.App.Avalonia.ViewModels;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Avalonia;

// Unit tests for the "本次运行" one-liner the host drops into
// the activity feed after every run. The summary has to stay
// scannable, so the tests cover the conditional rendering rules
// (silent when zero) and the duration formatting.
public class RunSummaryBuilderTests
{
    [Fact]
    public void BuildSummary_EmptyRun_ShowsOnlyDuration()
    {
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-3),
            CompletedAt = DateTimeOffset.UtcNow,
            FileChanges = [],
            ToolCallCount = 0,
            SubAgentRuns = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        // No files / no tools / no sub-agents → the bubble is just
        // a 3s timestamp. Avoids the "改 0 个文件 · 用 0 次工具"
        // noise that would imply something happened.
        Assert.Equal("3s", summary);
    }

    [Fact]
    public void BuildSummary_WithFileChanges_ShowsFileCount()
    {
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-12),
            CompletedAt = DateTimeOffset.UtcNow,
            FileChanges =
            [
                new AgentFileChange { Path = "a.cs" },
                new AgentFileChange { Path = "b.cs" },
                new AgentFileChange { Path = "c.cs" }
            ],
            ToolCallCount = 5,
            SubAgentRuns = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Contains("改 3 个文件", summary);
        Assert.Contains("用 5 次工具", summary);
        Assert.Contains("12s", summary);
    }

    [Fact]
    public void BuildSummary_WithSubAgents_ShowsSubAgentCount()
    {
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-45),
            CompletedAt = DateTimeOffset.UtcNow,
            FileChanges = [],
            ToolCallCount = 2,
            SubAgentRuns =
            [
                new AgentSubAgentRun { Id = "s1" },
                new AgentSubAgentRun { Id = "s2" }
            ]
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Contains("派 2 个子 Agent", summary);
    }

    [Fact]
    public void BuildSummary_LongDuration_FormatsAsMinutesAndSeconds()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(2).AddSeconds(15),
            FileChanges = [],
            ToolCallCount = 0,
            SubAgentRuns = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Equal("2m 15s", summary);
    }

    [Fact]
    public void BuildSummary_NoCompletionTime_ShowsUnknown()
    {
        // Shouldn't happen in practice (Run.Complete sets it), but
        // the formatter must not throw if CompletedAt is null —
        // render "未知时长" so the bubble stays readable.
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            FileChanges = [],
            ToolCallCount = 0,
            SubAgentRuns = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Equal("未知时长", summary);
    }

    [Fact]
    public void BuildSummary_SubSecondDuration_FormatsAsLessThanOneSecond()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMilliseconds(300),
            FileChanges = [],
            ToolCallCount = 0,
            SubAgentRuns = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Equal("<1s", summary);
    }

    [Fact]
    public void BuildSummary_WithVerifications_ShowsPassedOverTotal()
    {
        // When auto-verify ran, the summary surfaces the pass / total
        // count so the user can see whether the run's checks succeeded
        // without opening the agent log. The X/Y format mirrors the
        // status-bar context meter (the only other consumer of
        // Verifications) so the two surfaces stay in sync.
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-12);
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(12),
            FileChanges = [],
            ToolCallCount = 2,
            SubAgentRuns = [],
            Verifications = new()
            {
                new() { IsSuccess = true },
                new() { IsSuccess = true },
                new() { IsSuccess = false }
            }
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Equal("用 2 次工具 · 验证 2/3 通过 · 12s", summary);
    }

    [Fact]
    public void BuildSummary_ZeroVerifications_OmitsVerificationLine()
    {
        // No verifications means the line shouldn't appear at all —
        // same conditional rule as files / tools / sub-agents. The
        // user didn't run auto-verify, so don't imply they did.
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var run = new AgentRun
        {
            Id = "r1",
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(2),
            FileChanges = [],
            ToolCallCount = 0,
            SubAgentRuns = [],
            Verifications = []
        };

        var summary = AgentRunnerViewModel.BuildRunSummary(run);

        Assert.Equal("2s", summary);
    }
}
