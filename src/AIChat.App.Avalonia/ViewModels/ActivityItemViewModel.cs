using AIChat.App.Avalonia.Composition;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One bubble in the conversation activity feed. Title is the
// "speaker" label ("你" / "AIChat" / any system message), Detail is
// the markdown body, Status is the short time/result chip ("10:23",
// "完成", "失败", "已停止"). The three IsX bools classify the row
// for the XAML so it can pick the right bubble style (right-aligned
// user card, left AI bubble with avatar dot, centered system line).
//
// The IsX flags derive from Title + Detail + Status rather than
// from an enum, so the strings stay free-form and the XAML stays
// declarative.
public sealed partial class ActivityItemViewModel(string title, string detail, string status) : ViewModelBase
{
    public string Title { get; } = title;
    public IBrush TextForeground { get; } = TokenBrush("TextBrush");

    [ObservableProperty]
    private string detail = detail;

    [ObservableProperty]
    private string status = status;

    // The "thinking" state is: an assistant bubble that has not yet received
    // any content from the model. The XAML renders three animated dots
    // instead of the detail markdown, so the user always knows the run is
    // in flight.
    public bool IsThinking => Title == "AIChat" && string.IsNullOrEmpty(Detail) && Status == "运行中";

    // Bubble classification: the 1.0 Beta redesign needs three distinct
    // bubble styles (user right-aligned, AI with avatar, system centered),
    // and the XAML can't switch on Title in a binding. These flags make
    // the templates declarative.
    public bool IsUserBubble => Title == "你";
    public bool IsAssistantBubble => Title == "AIChat";
    public bool IsSystemBubble => !IsUserBubble && !IsAssistantBubble;

    // Set by the agent runner when the first content delta lands for
    // this bubble, so the runner can REPLACE the "正在启动任务..."
    // placeholder rather than appending to it (which would render as
    // "正在启动任务...Hello there..." in the markdown view). Stays
    // false for non-assistant bubbles — they never have a streaming
    // placeholder to clear.
    [ObservableProperty]
    private bool hasReceivedFirstContent;

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(IsThinking));
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsThinking));
}
