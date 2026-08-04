using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Unit tests for ActivityItemViewModel. Covers the bubble-classification
// flags + the HasReceivedFirstContent streaming flag the agent
// runner uses to clear the "正在启动任务..." placeholder on the
// first content delta (so the user sees the model's actual
// response, not "正在启动任务...Hello there...").
public class ActivityItemViewModelTests
{
    [Fact]
    public void IsThinking_True_WhenAssistantBubbleEmptyAndRunning()
    {
        var vm = new ActivityItemViewModel("AIChat", "", "运行中");
        Assert.True(vm.IsThinking);
    }

    [Fact]
    public void IsThinking_False_WhenDetailNonEmpty()
    {
        var vm = new ActivityItemViewModel("AIChat", "Hello there...", "运行中");
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public void IsThinking_False_WhenNotRunning()
    {
        // The placeholder only matters while the run is in flight.
        // A completed assistant bubble with no detail is unusual
        // (the runner drops a "本次运行已结束，但没有可显示的文本。"
        // message in that case) but should not render the thinking
        // dots.
        var vm = new ActivityItemViewModel("AIChat", "", "完成");
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public void IsUserBubble_True_WhenTitleIsYou()
    {
        var vm = new ActivityItemViewModel("你", "prompt", "已发送");
        Assert.True(vm.IsUserBubble);
        Assert.False(vm.IsAssistantBubble);
        Assert.False(vm.IsSystemBubble);
    }

    [Fact]
    public void IsAssistantBubble_True_WhenTitleIsAIChat()
    {
        var vm = new ActivityItemViewModel("AIChat", "response", "完成");
        Assert.False(vm.IsUserBubble);
        Assert.True(vm.IsAssistantBubble);
        Assert.False(vm.IsSystemBubble);
    }

    [Fact]
    public void IsSystemBubble_True_ForAnyOtherTitle()
    {
        // "本次运行", "已跳过操作", "需要任务", etc. all classify as
        // system bubbles — they're centered in the feed and use
        // muted text, not the user / assistant card styles.
        Assert.True(new ActivityItemViewModel("本次运行", "summary", "完成").IsSystemBubble);
        Assert.True(new ActivityItemViewModel("已跳过操作", "tool rejected", "已阻止").IsSystemBubble);
        Assert.True(new ActivityItemViewModel("需要任务", "enter a task", "等待").IsSystemBubble);
    }

    [Fact]
    public void HasReceivedFirstContent_DefaultsFalse()
    {
        // The agent runner uses this flag to decide whether to
        // REPLACE the bubble's Detail (first delta) or APPEND
        // (subsequent deltas). Defaults to false on a fresh bubble;
        // the runner flips it to true the first time real content
        // lands so the "正在启动任务..." placeholder is cleared
        // instead of concatenated with the model's response.
        var vm = new ActivityItemViewModel("AIChat", "正在启动任务...", "运行中");
        Assert.False(vm.HasReceivedFirstContent);
    }

    [Fact]
    public void HasReceivedFirstContent_CanBeToggled()
    {
        // The runner writes to this from the dispatcher, so it
        // needs to be settable. The toggle is one-way (false →
        // true) — the runner only sets it true once per bubble.
        var vm = new ActivityItemViewModel("AIChat", "", "运行中");
        Assert.False(vm.HasReceivedFirstContent);
        vm.HasReceivedFirstContent = true;
        Assert.True(vm.HasReceivedFirstContent);
    }

    [Fact]
    public void Title_FlipReRaisesIsXBubbles_AndIsThinking()
    {
        // The host reuses ActivityItemViewModel rows for tool-approval
        // bubbles (dad6989, the earlier approval/test in-place update
        // sweep) and may flip Title from "需要确认" to "已允许操作"
        // when the user resolves. Title is the single source of truth
        // for IsUserBubble / IsAssistantBubble / IsSystemBubble +
        // IsThinking — without an explicit re-raise, the XAML
        // re-render keeps the old classification (the bubble that
        // started as a centered system row would stay system-styled
        // even after Title flipped to a user-facing row, etc).
        var vm = new ActivityItemViewModel("需要确认", "summary", "等待");
        Assert.True(vm.IsSystemBubble);
        Assert.False(vm.IsUserBubble);
        Assert.False(vm.IsAssistantBubble);

        var reRaised = new System.Collections.Generic.HashSet<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) reRaised.Add(e.PropertyName);
        };

        vm.Title = "已允许操作";
        Assert.Contains(nameof(ActivityItemViewModel.IsSystemBubble), reRaised);
        Assert.Contains(nameof(ActivityItemViewModel.IsUserBubble), reRaised);
        Assert.Contains(nameof(ActivityItemViewModel.IsAssistantBubble), reRaised);
        Assert.Contains(nameof(ActivityItemViewModel.IsThinking), reRaised);
    }

    [Fact]
    public void CanCopyAssistantBubble_True_WhenAssistantHasContent()
    {
        // 1.0.1: a completed AI bubble with a
        // non-empty Detail is the canonical
        // "ready to copy" state. The button's
        // IsVisible binding reads this flag.
        var vm = new ActivityItemViewModel("AIChat", "Hello there...", "完成");
        Assert.True(vm.CanCopyAssistantBubble);
    }

    [Fact]
    public void CanCopyAssistantBubble_False_WhileThinking()
    {
        // The thinking state is the 3-dot
        // placeholder, no real content yet —
        // copying the empty string would land
        // an empty clipboard and surprise the
        // user. Hide the button.
        var vm = new ActivityItemViewModel("AIChat", "", "运行中");
        Assert.True(vm.IsThinking);
        Assert.False(vm.CanCopyAssistantBubble);
    }

    [Fact]
    public void CanCopyAssistantBubble_False_ForUserBubbles()
    {
        // The user bubble has its own Button that
        // copies-to-composer-for-edit. The 复制
        // affordance is only for AI bubbles.
        var vm = new ActivityItemViewModel("你", "my prompt", "已发送");
        Assert.False(vm.CanCopyAssistantBubble);
    }

    [Fact]
    public void CanCopyAssistantBubble_False_ForSystemBubbles()
    {
        // System rows are centered / muted —
        // they don't carry user-facing content
        // the user would want to copy.
        var vm = new ActivityItemViewModel("本次运行", "summary text", "完成");
        Assert.False(vm.CanCopyAssistantBubble);
    }

    [Fact]
    public void CanCopyAssistantBubble_FlipsTrue_WhenDetailLands()
    {
        // The streaming runner starts an AI bubble
        // with empty Detail and Status="运行中"
        // (IsThinking=true), then sets Detail as
        // the first delta arrives. The 复制 button
        // should flip visible the moment Detail
        // becomes non-empty.
        var vm = new ActivityItemViewModel("AIChat", "", "运行中");
        Assert.False(vm.CanCopyAssistantBubble);
        vm.Detail = "first chunk";
        Assert.True(vm.CanCopyAssistantBubble);
    }

    [Fact]
    public void DetailChange_ReRaisesCanCopyAssistantBubble()
    {
        // The XAML's IsVisible binding listens to
        // PropertyChanged. Without a re-raise on
        // Detail change, the 复制 button stays
        // hidden the whole streaming run (Detail
        // is the only field that flips true → it
        // has to fire the notification).
        var vm = new ActivityItemViewModel("AIChat", "", "运行中");
        var reRaised = new System.Collections.Generic.HashSet<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) reRaised.Add(e.PropertyName);
        };
        vm.Detail = "hello";
        Assert.Contains(nameof(ActivityItemViewModel.CanCopyAssistantBubble), reRaised);
    }
}
