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
}
