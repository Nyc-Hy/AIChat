namespace AIChat.App.Avalonia.Composition;

// Severity for a transient toast notification shown at the bottom of
// the main window. Auto-dismissed after a few seconds; stacking up to
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

    void Dismiss(ToastItem item);
}

public sealed class ToastItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Message { get; init; } = "";
    public ToastLevel Level { get; init; } = ToastLevel.Info;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

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
