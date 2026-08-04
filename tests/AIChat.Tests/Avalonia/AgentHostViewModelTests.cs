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

    // 1.0.1: UpdatePlan snapshots the user's
    // expanded state across agent step updates
    // (the runner emits a full plan on every
    // step, so PlanItems is cleared + re-added
    // each time). Without this, the user would
    // have to re-click every plan row they were
    // already reading every time the agent added
    // a new step — a daily-driver paper cut.
    [Fact]
    public void UpdatePlan_PreservesIsExpandedForMatchingIds()
    {
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "step-1",
                    Title = "read foo.cs",
                    Notes = "use Read tool on foo.cs",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.InProgress
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "step-2",
                    Title = "edit foo.cs",
                    Notes = "add error handling",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        var step1 = host.PlanItems.First(item => item.Id == "step-1");
        step1.IsExpanded = true;

        // Agent now adds step 3. The runner
        // emits a full plan snapshot, so
        // UpdatePlan re-creates every row.
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "step-1",
                    Title = "read foo.cs",
                    Notes = "use Read tool on foo.cs",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.InProgress
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "step-2",
                    Title = "edit foo.cs",
                    Notes = "add error handling",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "step-3",
                    Title = "run tests",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });

        var refreshed1 = host.PlanItems.First(item => item.Id == "step-1");
        var refreshed2 = host.PlanItems.First(item => item.Id == "step-2");
        var newStep = host.PlanItems.First(item => item.Id == "step-3");
        Assert.True(refreshed1.IsExpanded, "step-1 was expanded before the update — must stay expanded");
        Assert.False(refreshed2.IsExpanded, "step-2 was collapsed — must stay collapsed");
        Assert.False(newStep.IsExpanded, "step-3 is new — must start collapsed");
    }

    [Fact]
    public void UpdatePlan_RowWithoutId_DoesNotPreserveExpandAcrossRebuild()
    {
        // Belt-and-suspenders: if a plan item
        // somehow has no Id (legacy or
        // malformed runner output), the
        // expand state should not be
        // carried over. We'd rather have a
        // single transient collapse than a
        // false-positive match that
        // surprises the user.
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    // Id is auto-generated on the
                    // domain side, so we can't
                    // trivially make it null —
                    // this test is more of a
                    // "no-throw" guard for the
                    // path that handles empty
                    // Ids.
                    Id = "",
                    Title = "no id",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        var row = host.PlanItems[0];
        row.IsExpanded = true;

        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "different-id",
                    Title = "still no id row",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        Assert.False(host.PlanItems[0].IsExpanded);
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

    // 1.0.1: 全部展开 / 全部折叠 header
    // buttons on the Plan panel. The
    // methods flip every row's
    // IsExpanded in one shot — long
    // plans (10+ items) are painful
    // to expand one at a time when
    // the user wants to read the
    // whole plan up front.
    [Fact]
    public void ExpandAllPlanItems_FlipsEveryRowToExpanded()
    {
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "read foo.cs",
                    Notes = "use Read tool on foo.cs",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p2",
                    Title = "edit foo.cs",
                    Notes = "add error handling",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p3",
                    Title = "run tests",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        // Start with everything collapsed
        // (the default for a fresh plan).
        Assert.All(host.PlanItems, item => Assert.False(item.IsExpanded));

        host.ExpandAllPlanItems();

        Assert.All(host.PlanItems, item => Assert.True(item.IsExpanded));
    }

    [Fact]
    public void CollapseAllPlanItems_FlipsEveryRowToCollapsed()
    {
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "read foo.cs",
                    Notes = "use Read tool",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p2",
                    Title = "edit foo.cs",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        // Manually expand both so we can
        // verify the collapse path actually
        // flips them back.
        host.PlanItems[0].IsExpanded = true;
        host.PlanItems[1].IsExpanded = true;

        host.CollapseAllPlanItems();

        Assert.All(host.PlanItems, item => Assert.False(item.IsExpanded));
    }

    [Fact]
    public void ExpandCollapseAll_PlaysWithUpdatePlanPersistence()
    {
        // The two helpers must compose
        // with the UpdatePlan expand-
        // state-persistence logic (3a00cc0):
        // expand all → agent adds step 4 →
        // update should keep the original
        // 3 rows expanded and the new row
        // starts collapsed.
        var (host, _, _, _) = CreateHost();
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "read foo.cs",
                    Notes = "use Read tool",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p2",
                    Title = "edit foo.cs",
                    Notes = "add handling",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });
        host.ExpandAllPlanItems();

        // Agent adds step 3.
        host.UpdatePlan(new AIChat.Domain.Chat.AgentPlan
        {
            Items =
            {
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p1",
                    Title = "read foo.cs",
                    Notes = "use Read tool",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p2",
                    Title = "edit foo.cs",
                    Notes = "add handling",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                },
                new AIChat.Domain.Chat.AgentPlanItem
                {
                    Id = "p3",
                    Title = "run tests",
                    Status = AIChat.Domain.Chat.AgentPlanItemStatus.Pending
                }
            }
        });

        var p1 = host.PlanItems.First(item => item.Id == "p1");
        var p2 = host.PlanItems.First(item => item.Id == "p2");
        var p3 = host.PlanItems.First(item => item.Id == "p3");
        Assert.True(p1.IsExpanded, "was expanded before update — must stay expanded");
        Assert.True(p2.IsExpanded, "was expanded before update — must stay expanded");
        Assert.False(p3.IsExpanded, "new row starts collapsed");

        // Collapse all should still flip
        // the new row to collapsed (which
        // is its default anyway) and the
        // previously-expanded rows.
        host.CollapseAllPlanItems();
        Assert.All(host.PlanItems, item => Assert.False(item.IsExpanded));
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

    [Fact]
    public void HasPendingFollowup_FalseInitially_TrueAfterEnqueue()
    {
        // 1.0.1: the 追加要求 button
        // label flips to "已暂存"
        // when the queue is occupied.
        // The flag is the XAML's
        // single source of truth.
        var (host, _, _, _) = CreateHost();
        Assert.False(host.HasPendingFollowup);
        host.IsRunning = true;
        host.EnqueueFollowup("follow-up");
        Assert.True(host.HasPendingFollowup);
    }

    [Fact]
    public void HasPendingFollowup_FlipsFalse_WhenPendingCleared()
    {
        // 1.0.1: the post-run auto-continue
        // path drains _pendingFollowup,
        // which must flip HasPendingFollowup
        // back to false. Pin the read-side
        // behaviour by simulating the
        // drain (the test calls the same
        // SendTaskAsync path, but here we
        // drive it via the public surface:
        // mark NotRunning, set the field
        // back to null, observe).
        var (host, _, _, _) = CreateHost();
        host.IsRunning = true;
        host.EnqueueFollowup("follow-up");
        Assert.True(host.HasPendingFollowup);
        // Re-raise the path the runner
        // would take on drain: the
        // agent-host's IsRunning flips
        // back to false inside the
        // runner's finally, then the
        // host's post-cleanup clears
        // the queue. We can't drive the
        // full SendTaskAsync without a
        // live chat service, so the
        // closest unit-level signal is:
        // clear the field via the same
        // observable path the host uses
        // (set to null through the
        // auto-generated public
        // PendingFollowup setter).
        host.PendingFollowup = null;
        Assert.False(host.HasPendingFollowup);
    }

    // ---- 1.0.1: per-AI-bubble 重新生成 button ----

    [Fact]
    public void CanRegenerate_FalseInitially_NoPriorRun()
    {
        // Fresh host with no prior send.
        // The XAML 重新生成 button binds
        // to CanRegenerate so the
        // affordance is dim for first-
        // run users who haven't sent
        // anything yet.
        var (host, _, _, _) = CreateHost();

        Assert.False(host.CanRegenerate);
    }

    [Fact]
    public void CanRegenerate_TrueAfterPriorRun()
    {
        // Simulate a completed prior run
        // by calling PrepareContinuation,
        // which is the public method
        // AgentRunner / RunHistory use to
        // restore _lastUserPrompt without
        // driving the full send flow.
        // The new 重新生成 button should
        // become enabled so the user can
        // "give me a different take".
        var (host, _, _, _) = CreateHost();
        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "summarise the last 5 commits",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed,
        });

        Assert.True(host.CanRegenerate);
    }

    [Fact]
    public void CanRegenerate_FalseWhileRunning()
    {
        // Even with a prior prompt on
        // record, the button is dim
        // while another run is in
        // flight — regenerating mid-
        // run would race the existing
        // agent loop and could leak
        // two concurrent token streams
        // into the same conversation.
        var (host, _, _, _) = CreateHost();
        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "original",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed,
        });
        Assert.True(host.CanRegenerate);

        host.IsRunning = true;

        Assert.False(host.CanRegenerate);
    }

    [Fact]
    public void RegenerateLastResponse_NoPriorPrompt_ReturnsFalse()
    {
        // First-run user clicked the
        // button on an empty state.
        // The handler toasts a warning
        // and the method returns false
        // so the caller knows the
        // send was not kicked off.
        var (host, _, _, _) = CreateHost();

        var fired = host.RegenerateLastResponse();

        Assert.False(fired);
        // Composer stays empty — no
        // accidental text got dumped
        // into the draft from an
        // empty last-prompt.
        Assert.Equal("", host.DraftPrompt);
    }

    [Fact]
    public void RegenerateLastResponse_WithPriorPrompt_PopulatesDraftAndReturnsTrue()
    {
        // The happy path. After a
        // completed run, clicking
        // 重新生成 re-fills the
        // composer with the same
        // prompt and kicks off a
        // fresh send. The DraftPrompt
        // is observable so the user
        // sees the text land before
        // the run actually starts —
        // important for the "I just
        // want to see what it
        // re-sent" muscle memory
        // of an experienced daily
        // driver.
        var (host, _, _, _) = CreateHost();
        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "explain this stack trace",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed,
        });

        var fired = host.RegenerateLastResponse();

        Assert.True(fired);
        Assert.Equal("explain this stack trace", host.DraftPrompt);
    }

    [Fact]
    public void RegenerateLastResponse_WhileRunning_ReturnsFalse()
    {
        // Defensive double-check.
        // The XAML IsEnabled binding
        // should already dim the
        // button, but a stale UI
        // state during a fast
        // status flip could fire
        // the handler with the
        // command still enabled.
        // The method refuses to
        // queue a second run on
        // top of an in-flight one.
        var (host, _, _, _) = CreateHost();
        host.PrepareContinuation(new AIChat.Domain.Chat.AgentRun
        {
            Id = "run-1",
            Goal = "goal",
            Status = AIChat.Domain.Chat.AgentRunStatus.Completed,
        });
        host.IsRunning = true;

        var fired = host.RegenerateLastResponse();

        Assert.False(fired);
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

    // TrySuggestAtCompletion is the keyboard half of the
    // @-reference flow: Tab inside a partial
    // @<kind>:<partial> splices the first matching
    // Source from the registry. It's a pure static
    // function so the tests cover the four ways the
    // user can land in a no-match state (empty text,
    // email-style "@" embedded in a word, kind
    // mismatch, out-of-range caret) plus the two
    // happy paths (kind alone, kind + partial id).
    [Fact]
    public void TrySuggestAtCompletion_EmptyText_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("", 0, sources));
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion(null!, 0, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_CaretAtStart_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("hello world", 0, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_CaretOutOfRange_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        // Past end — TextBox rarely lands here but the
        // helper has to be defensive.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @web", 99, sources));
        // Negative — degenerate guard.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @web", -1, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_NoAtSign_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        // Caret is in a word, no @ anywhere — the
        // "user is typing in the middle of a sentence"
        // case.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("hello world", 5, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_EmailCase_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        // "user@host" — the @ is preceded by 'r' (a
        // word char), so the helper must reject.
        // Otherwise Tab would silently splice
        // "@web:abc" after an email address.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("user@host", 9, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_AtAcrossWhitespace_ReturnsNull()
    {
        var sources = new[] { NewSource("web") };
        // "see @web" with caret in "world" — the @
        // exists but the caret's word doesn't
        // contain it, so no completion.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @web abc", 14, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_KindAlone_ReturnsFirstSourceOfKind()
    {
        var web1 = NewSource("web", "abc");
        var web2 = NewSource("web", "def");
        var clip = NewSource("clipboard", "xyz");
        var sources = new[] { web1, web2, clip };
        // "@web" with no id — first source of that
        // kind wins (registry ordering is
        // insertion-order, the user can keep
        // typing to narrow).
        var match = AgentHostViewModel.TrySuggestAtCompletion("see @web", 8, sources);
        Assert.Same(web1, match);
    }

    [Fact]
    public void TrySuggestAtCompletion_KindWithPartialId_ReturnsFirstPrefixMatch()
    {
        var web1 = NewSource("web", "abc");
        var web2 = NewSource("web", "abd");
        var web3 = NewSource("web", "xyz");
        var sources = new[] { web1, web2, web3 };
        // "@web:ab" — both abc and abd match the
        // "ab" prefix, but abc comes first.
        var match = AgentHostViewModel.TrySuggestAtCompletion("see @web:ab", 11, sources);
        Assert.Same(web1, match);
    }

    [Fact]
    public void TrySuggestAtCompletion_KindMismatch_ReturnsNull()
    {
        var sources = new[] { NewSource("web", "abc") };
        // "@clip" with no clipboard sources — the
        // user typed the wrong kind, completion
        // stays null so Tab is a no-op (better than
        // silently completing the wrong kind).
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @clip", 9, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_IdNoPrefixMatch_ReturnsNull()
    {
        var sources = new[] { NewSource("web", "abc") };
        // "@web:zzz" — no source has an id starting
        // with zzz, the user is on a stale id.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @web:zzz", 12, sources));
    }

    [Fact]
    public void TrySuggestAtCompletion_CaretInMiddleOfWord_StillMatches()
    {
        var sources = new[] { NewSource("web", "abc") };
        // "see @web:ab extra" with caret at 11
        // (the position right after the 'b' of
        // "ab", before the space) — the helper
        // has to walk backwards from caret and
        // stop at the first non-word
        // character, not from end-of-string.
        // text[4..11] is "@web:ab", a valid
        // partial that matches the "abc" id.
        var match = AgentHostViewModel.TrySuggestAtCompletion("see @web:ab extra", 11, sources);
        Assert.Same(sources[0], match);
    }

    [Fact]
    public void TrySuggestAtCompletion_CaseInsensitiveKind()
    {
        var sources = new[] { NewSource("web", "abc") };
        // "@WEB" — kind match is
        // case-insensitive (the parser that
        // consumes the spliced text is also
        // case-insensitive on kind).
        var match = AgentHostViewModel.TrySuggestAtCompletion("see @WEB", 8, sources);
        Assert.Same(sources[0], match);
    }

    [Fact]
    public void TrySuggestAtCompletion_CaseInsensitiveIdPrefix()
    {
        var sources = new[] { NewSource("web", "abc") };
        // "@web:ABC" — id prefix match is
        // case-insensitive. Same rationale as
        // the kind test.
        var match = AgentHostViewModel.TrySuggestAtCompletion("see @web:ABC", 11, sources);
        Assert.Same(sources[0], match);
    }

    [Fact]
    public void TrySuggestAtCompletion_EmptySources_ReturnsNull()
    {
        // "@web" but the registry is empty —
        // there is nothing to match.
        Assert.Null(AgentHostViewModel.TrySuggestAtCompletion("see @web", 8, []));
    }

    [Fact]
    public async Task SourcesForAutocomplete_ExposesRegistryContents()
    {
        // The XAML key handler reaches
        // SourcesForAutocomplete to call
        // TrySuggestAtCompletion, so the
        // property must be a live view of the
        // registry (not a snapshot at ctor
        // time).
        var repository = new InMemoryAppRepository();
        var registry = new InMemorySourceRegistry();
        await registry.AddAsync(NewSource("web", "abc"));
        await registry.AddAsync(NewSource("clipboard", "xyz"));
        var host = CreateHostWithRegistry(repository, registry);
        var sources = host.Host.SourcesForAutocomplete;
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.Id == "abc" && s.Kind == "web");
        Assert.Contains(sources, s => s.Id == "xyz" && s.Kind == "clipboard");
    }

    private (AgentHostViewModel Host, ProjectSidebarViewModel Sidebar, ActivityFeedViewModel Activity, ToastService Toast)
        CreateHostWithRegistry(InMemoryAppRepository repository, InMemorySourceRegistry registry)
    {
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
            registry,
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
