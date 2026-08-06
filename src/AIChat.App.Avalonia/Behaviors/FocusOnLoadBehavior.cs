using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

// The event-arg type lives in the global Avalonia namespace, not
// under a sub-folder. Because this file is in the AIChat.App.Avalonia
// tree, the compiler resolves the bare "Avalonia.X" form relative
// to our namespace and can't find it — so we `using Avalonia;` and
// reference the bare name.

namespace AIChat.App.Avalonia.Behaviors;

// Attached property that calls Focus() (and SelectAll() for
// TextBox) the first time the control is attached to the visual
// tree, AND every time the control transitions from hidden to
// visible. The second trigger matters for inline-rename patterns:
// the TextBox lives in the DataTemplate permanently, the XAML
// just toggles its IsVisible when the user picks "重命名".
// AttachedToVisualTree only fires on the first add, so the
// "rename again" case would never re-focus without the IsVisible
// watcher.
//
// Used as:
//   <TextBox FocusOnLoadBehavior.IsEnabled="True" .../>
//
// Posting to the UI thread at Input priority lets the layout
// pass settle (the control has to be measured/arranged before
// Focus() works) and lets the click that opened the menu finish
// dispatching so we don't yank focus mid-handler.
public static class FocusOnLoadBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(FocusOnLoadBehavior),
            defaultValue: false);

    public static void SetIsEnabled(Control element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control element) =>
        element.GetValue(IsEnabledProperty);

    static FocusOnLoadBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e is not AvaloniaPropertyChangedEventArgs<bool> typed ||
            !typed.NewValue.Value)
        {
            return;
        }

        // First-time attach (DataTemplate instantiate, or a real
        // control re-added after being torn down).
        sender.AttachedToVisualTree -= OnAttached;
        sender.AttachedToVisualTree += OnAttached;

        // Re-focus every time the control becomes visible. This
        // covers the "rename, commit, rename again" path where
        // the TextBox is already in the visual tree and only
        // IsVisible flipped back to true.
        sender.PropertyChanged -= OnVisibleChanged;
        sender.PropertyChanged += OnVisibleChanged;

        if (sender.IsVisible)
        {
            FocusNow(sender);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && control.IsVisible)
        {
            FocusNow(control);
        }
    }

    private static void OnVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty)
        {
            return;
        }
        if (sender is Control control && control.IsVisible)
        {
            FocusNow(control);
        }
    }

    private static void FocusNow(Control control)
    {
        Dispatcher.UIThread.Post(() =>
        {
            control.Focus();
            if (control is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }, DispatcherPriority.Input);
    }
}
