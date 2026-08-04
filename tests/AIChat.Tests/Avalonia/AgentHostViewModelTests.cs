using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Projects;
using AIChat.Tests.TestDoubles;
using Moq;

namespace AIChat.Tests.Avalonia;

public sealed class AgentHostViewModelTests : IDisposable
{
    private readonly string _projectPath = Path.Combine(
        Path.GetTempPath(),
        "AIChatAgentHostTests",
        Guid.NewGuid().ToString("N"));

    public AgentHostViewModelTests() => Directory.CreateDirectory(_projectPath);

    [Fact]
    public void CanRunVerification_TracksProjectAndRunState()
    {
        var (host, sidebar, _, _) = CreateHost();
        Assert.False(host.CanRunVerification);

        sidebar.Refresh([CreateProject()]);
        Assert.True(host.CanRunVerification);

        host.IsRunning = true;
        Assert.False(host.CanRunVerification);
        host.IsRunning = false;
        Assert.True(host.CanRunVerification);

        host.IsVerifying = true;
        Assert.False(host.CanRunVerification);
        host.IsVerifying = false;
        sidebar.Refresh([]);
        Assert.False(host.CanRunVerification);
    }

    [Fact]
    public async Task RunVerification_BlockedCommandSurfacesFailureAndResetsState()
    {
        var (host, sidebar, activity, toast) = CreateHost();
        sidebar.Refresh([CreateProject()]);

        await host.RunVerificationCommand.ExecuteAsync(null);

        Assert.False(host.IsVerifying);
        var result = Assert.Single(activity.Activity, item => item.Title == "验证：unsafe");
        Assert.Equal("失败", result.Status);
        Assert.Contains(toast.Toasts, item =>
            item.Level == ToastLevel.Warning && item.Message.Contains("部分验证失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportConversationPersistenceFailure_AddsDurableFeedback()
    {
        var (host, _, activity, toast) = CreateHost();

        await host.ReportConversationPersistenceFailureAsync();

        var item = Assert.Single(activity.Activity);
        Assert.Equal("对话保存失败", item.Title);
        Assert.Equal("警告", item.Status);
        Assert.Contains(toast.Toasts, toastItem => toastItem.Level == ToastLevel.Warning);
    }

    [Fact]
    public void ContextBudgetDetails_LabelsEstimateAndAvoidsBillingClaim()
    {
        var (host, _, _, _) = CreateHost();
        host.InputTokens = 250_000;

        Assert.Contains("25%", host.ContextBudgetDetails);
        Assert.Contains("本地路由估算", host.ContextBudgetDetails);
        Assert.Contains("不是提供方计费 usage", host.ContextBudgetDetails);
    }

    [Fact]
    public void PrepareContinuation_ClearsComposerForNewInstruction()
    {
        var (host, _, _, _) = CreateHost();
        host.DraftPrompt = "old draft";

        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "original goal",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed
        });

        Assert.Equal("", host.DraftPrompt);
    }

    // ---- 1.0.1: plan items surface Notes + IsExpanded ----

    [Fact]
    public void PlanItem_DefaultsToCollapsedAndToggles()
    {
        // Same single-state-per-row pattern the
        // SourceRowViewModel uses. Two plan
        // rows can be expanded at once (useful
        // for comparing a "read file X" step
        // against an "edit file X" step).
        var item = new PlanItemViewModel
        {
            Title = "read foo.cs",
            Status = AIChat.Domain.Chat.AgentPlanItemStatus.InProgress,
        };
        Assert.False(item.IsExpanded);
        item.IsExpanded = !item.IsExpanded;
        Assert.True(item.IsExpanded);
    }

    [Fact]
    public void PlanItem_HasNotes_DerivesFromString()
    {
        // The XAML row's IsEnabled binding is
        // wired to HasNotes so empty-Notes rows
        // are inert — no clickable area, no
        // expand affordance. The user sees a
        // single-line title and that's it.
        var empty = new PlanItemViewModel
        {
            Title = "short step",
            Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending,
        };
        Assert.False(empty.HasNotes);
        var withNotes = new PlanItemViewModel
        {
            Title = "long step",
            Notes = "use Read tool on /Users/me/foo.cs",
            Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending,
        };
        Assert.True(withNotes.HasNotes);
    }

    // ---- 1.0.1: send button is dim when the composer is empty ----

    [Fact]
    public void SendTaskCommand_Disabled_WhenDraftPromptIsEmpty()
    {
        // The XAML send button and the ⌘↵ key
        // path both gate on CanSendTask, which
        // now also requires a non-empty
        // DraftPrompt. Empty composer + click
        // = no-op (the XAML button is dim,
        // the key path is a no-op).
        var (host, _, _, _) = CreateHost();
        Assert.False(host.SendTaskCommand.CanExecute(null));
    }

    [Fact]
    public void SendTaskCommand_Enabled_WhenDraftPromptHasContent()
    {
        var (host, _, _, _) = CreateHost();
        host.DraftPrompt = "summarise the last 5 commits";
        Assert.True(host.SendTaskCommand.CanExecute(null));
    }

    [Fact]
    public void SendTaskCommand_ReEvaluatesAsDraftPromptChanges()
    {
        // OnDraftPromptChanged calls
        // NotifyCanExecuteChanged so the user
        // sees the send button dim / un-dim
        // as they clear / type in the
        // composer without having to first
        // toggle some other piece of state.
        var (host, _, _, _) = CreateHost();
        host.DraftPrompt = "hello";
        Assert.True(host.SendTaskCommand.CanExecute(null));
        host.DraftPrompt = "";
        Assert.False(host.SendTaskCommand.CanExecute(null));
        host.DraftPrompt = "   ";
        Assert.False(host.SendTaskCommand.CanExecute(null));
    }

    // ---- 1.0.1: ClearRunState wipes plan + sub-agent + status
    //              surface when the user switches conversations ----

    [Fact]
    public void ClearRunState_EmptiesPlanItemsAndSubAgentRuns()
    {
        // The previous shape only cleared
        // the activity feed on conversation
        // switch — the plan panel (bound
        // to AgentHost.PlanItems) and the
        // sub-agent section (bound to
        // SubAgentRuns) kept their last
        // values, so the user saw the
        // previous conversation's plan
        // steps still rendered above the
        // freshly-loaded activity feed.
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "read foo.cs",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.InProgress
                }
            }
        });
        host.UpsertSubAgentRun(new AIChat.Domain.Chat.AgentSubAgentRun
        {
            Id = "s1",
            TemplateId = "explore",
            Task = "x",
            Status = "running"
        });
        Assert.NotEmpty(host.PlanItems);
        Assert.NotEmpty(host.SubAgentRuns);

        host.ClearRunState();

        Assert.Empty(host.PlanItems);
        Assert.Empty(host.SubAgentRuns);
    }

    [Fact]
    public void ClearRunState_ResetsLastAssistantStatusAndInputTokens()
    {
        // The status bar's "已完成" / "失败"
        // pill and the context-budget
        // estimate both read from the host
        // (not from the per-conversation
        // activity feed). Without this
        // reset, switching to a fresh
        // conversation shows the previous
        // conversation's "失败" pill and
        // a stale token count.
        var (host, _, _, _) = CreateHost();
        host.LastAssistantStatus = "失败";
        host.InputTokens = 12_345;

        host.ClearRunState();

        Assert.Equal("", host.LastAssistantStatus);
        Assert.Equal(0, host.InputTokens);
    }

    [Fact]
    public void ClearRunState_PreservesIsRunning()
    {
        // A run that's actually in flight
        // when the user clicks another
        // conversation (e.g. they kicked
        // off a long task and walked away
        // from the app, came back, and
        // switched conversations to
        // compare with an older one) must
        // stay running. Forcing
        // IsRunning=false would race the
        // actual run continuation and
        // leave the new conversation's
        // composer in a stuck "can't
        // send" state.
        var (host, _, _, _) = CreateHost();
        host.IsRunning = true;
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "still running",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.InProgress
                }
            }
        });

        host.ClearRunState();

        Assert.True(host.IsRunning);
    }

    // ---- 1.0.1: 追加要求 (queue + auto-continue) ----

    [Fact]
    public void EnqueueFollowup_RejectsWhenNotRunning()
    {
        // The 追加要求 button's affordance
        // only makes sense while a run is
        // in flight. If a future caller
        // races the IsRunning flip, the
        // public method guards in addition
        // to the button's IsEnabled binding.
        var (host, _, _, _) = CreateHost();
        Assert.False(host.IsRunning);
        Assert.False(host.EnqueueFollowup("follow-up text"));
    }

    [Fact]
    public void EnqueueFollowup_RejectsEmptyOrWhitespace()
    {
        var (host, _, _, _) = CreateHost();
        host.IsRunning = true;
        Assert.False(host.EnqueueFollowup(""));
        Assert.False(host.EnqueueFollowup("   "));
        Assert.False(host.EnqueueFollowup(null));
    }

    [Fact]
    public void EnqueueFollowup_TrimsAndAcceptsValidPrompt()
    {
        var (host, _, activity, _) = CreateHost();
        host.IsRunning = true;
        var accepted = host.EnqueueFollowup("  add context  ");
        Assert.True(accepted);
        // The activity feed should have
        // a system bubble so the user
        // sees the click landed (and
        // can verify the truncation
        // isn't hiding anything important).
        var followUpBubble = activity.Activity.LastOrDefault();
        Assert.NotNull(followUpBubble);
        Assert.Equal("已暂存追加", followUpBubble.Title);
    }

    [Fact]
    public void EnqueueFollowup_OverwritesPreviousPendingFollowup()
    {
        // The queue is one-deep on
        // purpose — a daily-driver user
        // who clicks 追加要求 twice
        // while a run is in flight
        // actually means "follow-up to
        // a follow-up", not "queue two
        // more". Last-write-wins matches
        // the daily-driver mental model.
        // The post-run auto-continue
        // path that drains _pendingFollowup
        // lives in SendTaskAsync and is
        // exercised end-to-end by the
        // integration tests; this unit
        // test only asserts the
        // accept-while-true contract.
        var (host, _, _, _) = CreateHost();
        host.IsRunning = true;
        Assert.True(host.EnqueueFollowup("first"));
        Assert.True(host.EnqueueFollowup("second"));
    }

    // ---- 1.0.1: insert @-reference at composer caret ----

    [Fact]
    public void InsertSourceReferenceAtCaret_EmptyPrompt_LandsAtStart()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");

        host.InsertSourceReferenceAtCaret(source, 0);

        Assert.Equal("@web:abc", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_AtStart_AppendsTrailingSpace()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        host.DraftPrompt = "hello";

        host.InsertSourceReferenceAtCaret(source, 0);

        // Caret sits at offset 0 — no char to the
        // left to fuse onto, so the method emits
        // just the trailing separator (the
        // reference and the next word would
        // otherwise glue together as
        // "@web:abchello"). No leading space
        // because there's no left neighbour.
        Assert.Equal("@web:abc hello", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_AtMid_SplicesAtCaret()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        host.DraftPrompt = "prefix suffix";

        // Caret at offset 6 sits between "prefix"
        // and the space; the splice should land the
        // reference between the two words with a
        // leading space (the previous char is "x",
        // not whitespace) and a trailing space (the
        // next char is "s" of "suffix", not
        // whitespace). The space inside the
        // original prompt is preserved on the
        // right-hand side.
        host.InsertSourceReferenceAtCaret(source, 6);

        Assert.Equal("prefix @web:abc suffix", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_AtEnd_AppendsWithLeadingSpace()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        host.DraftPrompt = "hello";

        host.InsertSourceReferenceAtCaret(source, host.DraftPrompt.Length);

        // Leading space because the previous char
        // is non-whitespace; no trailing space
        // because nothing follows.
        Assert.Equal("hello @web:abc", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_StaleCaret_ClampsToEnd()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        host.DraftPrompt = "hi";

        // Caret 999 is stale (the user deleted text
        // after a previous click landed at a
        // higher offset). The method clamps so
        // the splice doesn't throw.
        host.InsertSourceReferenceAtCaret(source, 999);

        Assert.Equal("hi @web:abc", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_Duplicate_IsNoOp()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        host.DraftPrompt = "see @web:abc for details";

        // Clicking the "引用" button twice with
        // the caret parked at the same spot
        // shouldn't add a second copy.
        host.InsertSourceReferenceAtCaret(source, host.DraftPrompt.Length);

        Assert.Equal("see @web:abc for details", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_DifferentSource_AppendsNormally()
    {
        var (host, _, _, _) = CreateHost();
        var web = NewSource("web", id: "a");
        var clip = NewSource("clipboard", id: "b");
        host.DraftPrompt = "see @web:a for context";

        host.InsertSourceReferenceAtCaret(clip, host.DraftPrompt.Length);

        // Dedup is per-source — a different
        // Source.Id with a different Kind is a
        // fresh reference.
        Assert.Equal("see @web:a for context @clipboard:b", host.DraftPrompt);
    }

    [Fact]
    public void InsertSourceReferenceAtCaret_AtWordBoundary_OmitsExtraSpaces()
    {
        var (host, _, _, _) = CreateHost();
        var source = NewSource("web");
        // Caret lands right after the space —
        // both sides are whitespace, so the
        // method shouldn't add *another* pair of
        // spaces.
        host.DraftPrompt = "hello world";

        host.InsertSourceReferenceAtCaret(source, 6);

        Assert.Equal("hello @web:abc world", host.DraftPrompt);
    }

    private static AIChat.Domain.Sources.Source NewSource(
        string kind,
        string id = "abc")
    {
        return new AIChat.Domain.Sources.Source
        {
            Id = id,
            Kind = kind,
            DisplayName = "Test source",
            Content = "test body",
        };
    }

    private (AgentHostViewModel Host, ProjectSidebarViewModel Sidebar, ActivityFeedViewModel Activity, ToastService Toast)
        CreateHost()
    {
        var repository = new InMemoryAppRepository();
        var settingsHolder = new SettingsHolder();
        settingsHolder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, settingsHolder);
        var activity = new ActivityFeedViewModel();
        var toast = new ToastService(action => action());
        var host = new AgentHostViewModel(
            Mock.Of<IChatCompletionService>(),
            AgentToolRegistry.CreateForTests([]),
            Mock.Of<IApprovalService>(),
            repository,
            sidebar,
            new ConversationListViewModel(repository),
            activity,
            toast,
            new InMemorySourceRegistry(),
            _ => { },
            () => settingsHolder.Current,
            () => false,
            () => false,
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        return (host, sidebar, activity, toast);
    }

    private WorkspaceProject CreateProject() => new()
    {
        Id = "project-1",
        Name = "Project",
        Folders = [new WorkspaceFolder { Id = "primary-1", Path = _projectPath }],
        PrimaryFolderId = "primary-1",
        VerificationCommands =
        [
            new ProjectVerificationCommand
            {
                Id = "unsafe",
                Name = "unsafe",
                Command = "rm -rf /"
            }
        ]
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectPath, recursive: true);
        }
        catch
        {
        }
    }
}
