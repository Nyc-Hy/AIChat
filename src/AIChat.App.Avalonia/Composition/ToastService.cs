using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace AIChat.App.Avalonia.Composition;

// Default toast surface. Holds an observable collection that the
// MainWindow binds to; the XAML renders one Border per entry. Each
// Show call schedules a 3-second auto-dismiss on the thread-pool.
// All collection mutations are marshalled to the UI thread so the
// ObservableCollection binding stays valid no matter who called in.
public sealed class ToastService : IToastService
{
    private const int MaxStacked = 3;
    private const int AutoDismissMs = 3000;

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        var item = new ToastItem { Message = message, Level = level };
        PostToUi(() =>
        {
            if (Toasts.Count >= MaxStacked)
            {
                Toasts.RemoveAt(0);
            }
            Toasts.Add(item);
        });
        _ = AutoDismissAsync(item);
    }

    public void Dismiss(ToastItem item)
    {
        PostToUi(() => Toasts.Remove(item));
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
