namespace AIChat.App.Avalonia.Composition;

// Severity for a transient toast notification shown at the bottom of the
// main window. Auto-dismissed after a few seconds; stacking up to
// three, with the most recent on top.
public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

// Transient notification surface for the desktop app. The view layer
// binds a panel to ToastItems and renders one Border per entry. The
// service does not own the UI; it appends/removes entries on the
// thread-pool, marshalled to the UI by the caller (the toast panel
// lives on the main window and is updated via a dispatcher).
public interface IToastService
{
    System.Collections.ObjectModel.ObservableCollection<ToastItem> Toasts { get; }

    void Show(string message, ToastLevel level = ToastLevel.Info);

    // 2026-08-06: a "toast with an inline action button". The XAML
    // shows a button labelled actionLabel next to the message; the
    // user clicking it fires onAction and dismisses the toast
    // immediately (so the action does not also have to clean up its
    // own state — the service is the only owner of the visible
    // surface). The auto-dismiss timer still runs, so an unused
    // action button times out like a plain toast.
    //
    // Used by the "delete conversation / project" paths to surface a
    // 5-second "已删除 X [撤销]" affordance — the user can rescue a
    // misclick before the timer fires. Action is a one-shot callback;
    // it does not run automatically on auto-dismiss (an unclicked
    // action is "user chose not to undo", not "user accepted the
    // default action").
    void ShowWithAction(
        string message,
        ToastLevel level,
        string actionLabel,
        Action onAction);

    void Dismiss(ToastItem item);
}

public sealed class ToastItem
{
    public string Message { get; init; } = "";
    public ToastLevel Level { get; init; } = ToastLevel.Info;

    // 1.0.6: optional inline action button. Empty ActionLabel
    // (the default for Show() callers) means the XAML renders no
    // button — the toast is message-only. Set via ShowWithAction().
    // OnAction fires when the user clicks the button; the XAML
    // dismisses the toast right after so the action can run without
    // worrying about its own visibility.
    public string ActionLabel { get; init; } = "";
    public Action? OnAction { get; init; }
    public bool HasAction => !string.IsNullOrEmpty(ActionLabel);

    // 1.0.1: dismiss callback the XAML click
    // handler can fire when the user wants to
    // close a toast immediately instead of
    // waiting for the 3s auto-dismiss. Wired
    // up by ToastService.Show (so the
    // service owns the back-edge to the
    // FIFO queue — the XAML just hands the
    // click back to the item, the item hands
    // the dismiss to the service, the
    // service promotes the next queued
    // toast). Null when the item is
    // constructed outside the service
    // (test double, etc.) — calling Dismiss
    // on a null-callback item is a no-op.
    public Action<ToastItem>? OnDismiss { get; init; }

    public void Dismiss() => OnDismiss?.Invoke(this);

    // 1.0.6: invoke the user-supplied action and dismiss the
    // toast. The XAML click handler calls this rather than
    // Dismiss() directly so the action runs before the surface
    // collapses — that way a slow action (e.g. restoring a
    // deleted conversation) does not show a half-torn-down toast
    // for the duration of its work. Safe to call when HasAction
    // is false (no-op).
    public void InvokeAction()
    {
        OnAction?.Invoke();
        OnDismiss?.Invoke(this);
    }

    // Per-level flags the XAML uses to colour the toast by severity.
    // One IsX per ToastLevel value; the XAML's Border picks up the
    // matching .toast-<level> class and the App.axaml style draws a
    // thin left-side stripe in the level's status colour so the user
    // can tell at a glance whether the message is a heads-up (info),
    // a confirmation (success), a "watch out" (warning), or a
    // "something broke" (error).
    public bool IsInfo => Level == ToastLevel.Info;
    public bool IsSuccess => Level == ToastLevel.Success;
    public bool IsWarning => Level == ToastLevel.Warning;
    public bool IsError => Level == ToastLevel.Error;
}
