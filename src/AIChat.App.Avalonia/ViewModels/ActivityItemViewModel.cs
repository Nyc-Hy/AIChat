using System.Collections.ObjectModel;
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
//
// 2026-08-05: thinking + tool-call consolidation. AI
// bubbles now carry two new fields beyond Detail:
//   - Thinking: the model-side reasoning chain
//     (M3's ``...`` blocks), parsed by
//     AIChat.Domain.Chat.ThinkBlockParser and
//     rendered as a collapsible "思考过程" section
//     above the answer. The chain is captured per
//     streaming delta so partial tags at chunk
//     boundaries don't leak into the visible
//     content.
//   - ToolCalls: ordered list of tool invocations
//     the model issued during the run. Renders as
//     a single expandable "工具调用 (N)" section
//     on the AI bubble instead of the previous
//     one-system-bubble-per-call layout that
//     flooded the activity feed on long runs
//     (10-tool runs were emitting 10–30+ center
//     "正在读取"/"工具问题" rows that pushed the
//     real conversation off-screen).
public sealed partial class ActivityItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string detail = "";

    [ObservableProperty]
    private string status = "";

    // 2026-08-05: extracted `` chain. The XAML
    // renders this as a collapsible "💭 思考过程
    // (Xs)" section above the answer when
    // non-empty. Empty for the user bubble (the
    // parser is AI-only) and for AI models that
    // don't emit think blocks (M2.7 reasoning
    // content goes through a separate field).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThinking))]
    [NotifyPropertyChangedFor(nameof(ThinkingSummary))]
    private string thinking = "";

    // 2026-08-05: tool call list. The agent
    // runner appends one entry per AgentRunEvent
    // type ToolCall; the XAML renders this as
    // a single expandable section on the AI
    // bubble (name + status + duration per row).
    // Replacing the previous "one system bubble
    // per call" pattern that cluttered the feed.
    public ObservableCollection<ToolCallRecord> ToolCalls { get; } = [];

    // 2026-08-05: per-run summary footer. The
    // agent runner used to drop this as a
    // separate "本次运行" system bubble between
    // the AI bubble and the next user message —
    // a centered, muted one-liner that
    // duplicated the bubble-status info the AI
    // bubble already shows. The bubble also
    // pushed long agent runs further from the
    // user's eye-line: user → AI → summary →
    // (next turn) instead of user → AI → (next
    // turn). Now the summary is a footer row
    // inside the AI bubble's StackPanel so the
    // conversation reads as a strict
    // user / AI / user / AI rhythm, with the
    // run stats anchored to the response that
    // produced them.
    //
    // Empty for the user bubble and for any
    // non-assistant bubble (the XAML's
    // IsVisible binds to HasRunSummary so the
    // footer line is hidden on bubbles that
    // have no run to summarise).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunSummary))]
    private string runSummary = "";

    public bool HasRunSummary => !string.IsNullOrWhiteSpace(RunSummary);

    public ActivityItemViewModel(string title, string detail, string status)
    {
        Title = title;
        Detail = detail;
        Status = status;
        // 2026-08-05: stamp the bubble at
        // construction so the XAML's
        // TimeDisplay can show a stable
        // "10:23" header. Set here
        // (rather than lazily derived
        // on first access) so a long
        // conversation that's been
        // scrolled back to keeps the
        // original timestamp even if
        // the user is reading the
        // bubble an hour later. The
        // XAML is a small "10:23"
        // prefix before the Status
        // text — daily drivers with
        // 5+ turns in a conversation
        // can finally see which turn
        // was when without having to
        // scroll back to the sidebar
        // conversation list.
        CreatedAt = DateTimeOffset.Now;
    }

    // 2026-08-05: when the bubble was
    // added. Stamped at construction
    // (see ctor) so a later read
    // returns the same value the
    // XAML rendered. Not observable
    // because it never changes after
    // construction — no need to
    // re-raise.
    private DateTimeOffset _createdAt;
    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        private set
        {
            if (_createdAt == value)
            {
                return;
            }
            _createdAt = value;
            OnPropertyChanged(nameof(CreatedAt));
            OnPropertyChanged(nameof(TimeDisplay));
        }
    }

    // Local time, hour:minute, 24-hour
    // format. Daily drivers compare
    // bubbles by relative time
    // ("上午 / 下午" is less useful
    // for a 30-minute coding session
    // than "10:23 → 10:51" tracking
    // how long each turn took). The
    // format is fixed-width-ish
    // (always 5 chars: "HH:mm") so
    // the column doesn't visually
    // jitter between single-digit
    // and double-digit hours.
    public string TimeDisplay => CreatedAt.ToString("HH:mm");

    // The "thinking" state is: an assistant bubble that has not yet received
    // any content from the model. The XAML renders three animated dots
    // instead of the detail markdown, so the user always knows the run is
    // in flight.
    public bool IsThinking => Title == "AIChat" && string.IsNullOrEmpty(Detail) && Status == "运行中";

    // Terminal states. The strings are produced by AgentRunner / harness
    // and consumed by the XAML via Classes.failed / Classes.stopped to
    // switch the bubble border + status-chip color. The two are split so
    // a user-initiated stop can read as amber (calm, intentional) while
    // a real error reads as red (something went wrong).
    public bool IsFailed => Status == "失败";
    public bool IsStopped => Status == "已停止";

    // Bubble classification: the 1.0 Beta redesign needs three distinct
    // bubble styles (user right-aligned, AI with avatar, system centered),
    // and the XAML can't switch on Title in a binding. These flags make
    // the templates declarative.
    public bool IsUserBubble => Title == "你";
    public bool IsAssistantBubble => Title == "AIChat";
    public bool IsSystemBubble => !IsUserBubble && !IsAssistantBubble;

    // 1.0.1: the AI bubble's "复制" button. The
    // button is hidden in three cases:
    //   - not an AI bubble (the XAML uses IsAssistantBubble
    //     to gate the whole AI template, but this property
    //     is the per-bubble IsVisible that keeps the
    //     button itself out of the way for in-flight /
    //     empty / system messages)
    //   - thinking (the 3-dot placeholder, no content yet)
    //   - empty detail (defensive — would land an empty
    //     string on the clipboard)
    // The Status column already shows the timestamp
    // ("12:34" / "完成" / "失败"), so a 复制 affordance
    // next to it is the one extra control an AI bubble
    // needs. Without it the user has to right-click the
    // MarkdownTextBlock, select all, copy — three steps
    // for a daily-driver action that should be one.
    public bool CanCopyAssistantBubble => IsAssistantBubble
        && !IsThinking
        && !string.IsNullOrWhiteSpace(Detail);

    // Set by the agent runner when the first content delta lands for
    // this bubble, so the runner can REPLACE the "正在启动任务..."
    // placeholder rather than appending to it (which would render as
    // "正在启动任务...Hello there..." in the markdown view). Stays
    // false for non-assistant bubbles — they never have a streaming
    // placeholder to clear.
    [ObservableProperty]
    private bool hasReceivedFirstContent;

    partial void OnTitleChanged(string value)
    {
        // Title drives the bubble classification (IsUserBubble /
        // IsAssistantBubble / IsSystemBubble) and the thinking
        // state, so a Title flip must re-raise all of them or
        // the XAML re-render uses the old classification.
        OnPropertyChanged(nameof(IsUserBubble));
        OnPropertyChanged(nameof(IsAssistantBubble));
        OnPropertyChanged(nameof(IsSystemBubble));
        OnPropertyChanged(nameof(IsThinking));
    }

    // 2026-08-05: derived helpers for the think-
    // section XAML. HasThinking is the IsVisible
    // gate; ThinkingSummary is the collapsed
    // preview text shown on the header row.
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);

    public string ThinkingSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Thinking))
            {
                return "";
            }
            // Trim to one line for the header
            // preview; the full chain is in the
            // expanded body. Falls back to the
            // first 80 chars when the chain has
            // no newline (most MiniMax think
            // blocks are short and one-line).
            var firstLine = Thinking.Split('\n')[0].Trim();
            if (firstLine.Length > 80)
            {
                firstLine = firstLine[..80] + "…";
            }
            return firstLine;
        }
    }

    // 2026-08-05: derived gate for the tool-call
    // section. The XAML uses ToolCalls.Count > 0
    // directly, but this property is the single
    // place the IsVisible binding lives so a
    // future rename / refactor is one edit not
    // ten.
    public bool HasToolCalls => ToolCalls.Count > 0;

    partial void OnThinkingChanged(string value)
    {
        // HasThinking + ThinkingSummary both
        // derive from the raw Thinking string,
        // so a write must re-raise both or the
        // header row stays stale mid-stream.
        OnPropertyChanged(nameof(HasThinking));
        OnPropertyChanged(nameof(ThinkingSummary));
    }

    partial void OnDetailChanged(string value)
    {
        // Detail drives IsThinking (the 3-dot
        // placeholder vs the rendered markdown) AND
        // CanCopyAssistantBubble (the 复制 button
        // gates on non-empty Detail). Re-raise both
        // so the XAML doesn't show a stale state
        // mid-stream.
        OnPropertyChanged(nameof(IsThinking));
        OnPropertyChanged(nameof(CanCopyAssistantBubble));
    }

    partial void OnStatusChanged(string value)
    {
        // Status drives the in-flight spinner AND the terminal
        // styling (IsFailed / IsStopped) so a Status flip must
        // re-raise all three or the XAML re-render uses stale
        // classification.
        OnPropertyChanged(nameof(IsThinking));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsStopped));
    }
}
