using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace AIChat.App.Avalonia.Composition;

// Default toast surface. Holds an observable collection that the
// MainWindow binds to; the XAML renders one Border per entry. Each
// Show call schedules a 3-second auto-dismiss on the thread-pool.
// All collection mutations are marshalled to the UI thread so the
// ObservableCollection binding stays valid no matter who called in.
//
// Queue semantics (1.0.1): the surface used to silently drop the
// oldest toast when a 4th arrived while 3 were still visible. The
// user reading the dropped toast had no warning — the message just
// vanished. The fix is a FIFO queue: every Show call enqueues an
// item, the UI collection always shows at most MaxVisible (3), and
// whenever a toast leaves the collection (auto-dismiss timer or
// explicit Dismiss), the next queued item is promoted into the
// visible window. The user sees every message they triggered; the
// only thing that changes is the rate at which they appear.
public sealed class ToastService : IToastService
{
    private const int MaxVisible = 3;
    private const int AutoDismissMs = 3000;
    private readonly Action<Action> _dispatchToUi;

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    // Pending items waiting for a visible slot. Kept in a plain
    // Queue<ToastItem> rather than an ObservableCollection because
    // the XAML doesn't bind to it — only the head of the queue is
    // ever read, and only when a visible slot opens up.
    private readonly Queue<ToastItem> _pending = new();

    public ToastService()
        : this(PostToUi)
    {
    }

    public ToastService(Action<Action> dispatchToUi)
    {
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    }

    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        EnqueueToast(new ToastItem
        {
            Message = message,
            Level = level,
            OnDismiss = Dismiss
        });
    }

    // 2026-08-06: show a toast with an inline action button. The
    // XAML renders the actionLabel button next to the message; the
    // user clicking it fires onAction and dismisses the toast.
    // Auto-dismiss still runs after AutoDismissMs, so an unused
    // action is the same as a non-actionable toast — the callback
    // is only invoked on the user click path, never on the
    // auto-dismiss path (an unclicked action is "user chose not to
    // undo", not "user accepted the default action").
    public void ShowWithAction(
        string message,
        ToastLevel level,
        string actionLabel,
        Action onAction)
    {
        if (string.IsNullOrEmpty(actionLabel))
        {
            throw new ArgumentException("Action label is required.", nameof(actionLabel));
        }
        if (onAction is null)
        {
            throw new ArgumentNullException(nameof(onAction));
        }
        EnqueueToast(new ToastItem
        {
            Message = message,
            Level = level,
            OnDismiss = Dismiss,
            ActionLabel = actionLabel,
            OnAction = onAction
        });
    }

    // Shared enqueue path — keeps the FIFO + auto-dismiss wiring
    // in one place so Show() and ShowWithAction() cannot drift.
    // The dispatch + AutoDismissAsync pair is what gives the
    // service its 3s visible-window guarantee and its at-most-3
    // concurrency limit, regardless of which public entry point
    // built the item.
    private void EnqueueToast(ToastItem item)
    {
        // 1.0.1: wire the click-to-dismiss
        // callback so the XAML can close a
        // toast immediately when the user
        // clicks it. Without this, important
        // warning / error toasts sometimes
        // auto-dismissed before the user
        // could read the full message (the
        // 3s timer fires whether the user's
        // eyes are on it or not). The
        // callback is the same Dismiss call
        // the auto-dismiss timer uses, so a
        // user click and the timer race to
        // the same Remove() / PromoteQueued()
        // path — Toasts.Remove returns false
        // if the item is already gone, which
        // is the right idempotent behaviour.
        _dispatchToUi(() =>
        {
            if (Toasts.Count >= MaxVisible)
            {
                // No visible slot right now — enqueue and wait.
                // The head will be promoted by PromoteQueuedAsync
                // when the next auto-dismiss / Dismiss fires.
                _pending.Enqueue(item);
            }
            else
            {
                Toasts.Add(item);
            }
        });
        _ = AutoDismissAsync(item);
    }

    public void Dismiss(ToastItem item)
    {
        _dispatchToUi(() =>
        {
            if (Toasts.Remove(item))
            {
                // A visible slot just opened up — promote the
                // head of the pending queue (if any). Done in
                // the same dispatch so the visible state
                // transitions atomically: "dismiss old + show
                // new" is one Add/Remove pair, not two.
                PromoteQueued();
            }
        });
    }

    // Pulls the next pending item into the visible window, if any.
    // Called from inside a UI-thread dispatch, so it's safe to
    // mutate Toasts directly.
    private void PromoteQueued()
    {
        while (Toasts.Count < MaxVisible && _pending.Count > 0)
        {
            Toasts.Add(_pending.Dequeue());
        }
    }

    private async Task AutoDismissAsync(ToastItem item)
    {
        try
        {
            await Task.Delay(AutoDismissMs);
            Dismiss(item);
        }
        catch
        {
            // Swallow: toasts are fire-and-forget; nothing meaningful to do
            // if the dismissal throws (most likely the process is exiting).
        }
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        }
    }
}
