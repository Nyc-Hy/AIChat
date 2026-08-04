using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.Views;

// Tiny modal that asks the user for a URL. Returns
// the trimmed string (or null on cancel) via the
// dialog's TaskCompletionSource. Used by the Wave 7
// "网页搜索" / AddWebSearchSource button — the
// underlying fetcher (IWebPageFetcher) needs a URL it
// can hit, and the existing file picker is the wrong
// affordance.
//
// The dialog is hand-rolled (no XAML) because the
// 1.0 design budget doesn't include a new view file
// for a 30-line input + 2 buttons, and the layout is
// constrained to "single text input, OK / Cancel" so
// the XAML file would be mostly markup noise. The
// About window in App.axaml.cs follows the same
// pattern (small inline Window, no XAML).
internal sealed class UrlInputDialog : Window
{
    private readonly TaskCompletionSource<string?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox _textBox;

    public UrlInputDialog(Window owner)
    {
        Title = "网页搜索";
        Width = 520;
        Height = 180;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // 1.0.1: drop the hard-coded white
        // background. The default Window
        // background follows the active
        // theme (SurfaceBrush at the app
        // level — light gray in light mode,
        // deep slate in dark mode), so the
        // dialog was previously a white
        // rectangle floating over a dark
        // page in ⌘⇧T dark mode. The same
        // default carries the text / muted
        // text colors (TextBrush +
        // MutedBrush below) so the dialog
        // stays readable in both modes
        // without a per-mode code path.

        _textBox = new TextBox
        {
            PlaceholderText = "https://example.com/article",
            FontSize = 14,
            Padding = new Thickness(8),
        };
        // Enter on the text box submits; Escape
        // cancels. The shortcuts match the
        // RunHistoryView's input-row pattern so the
        // user gets a consistent modal feel.
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        };

        var okButton = new Button
        {
            Content = "抓取",
            IsDefault = true,
            Padding = new Thickness(14, 6),
        };
        okButton.Click += (_, _) => Confirm();

        var cancelButton = new Button
        {
            Content = "取消",
            IsCancel = true,
            Padding = new Thickness(14, 6),
        };
        cancelButton.Click += (_, _) => Cancel();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, okButton },
        };

        // Theme-aware text colors. The two
        // hard-coded brush literals below
        // were the only light-mode references
        // left in the project; replaced with
        // the same DynamicResource brushes
        // the XAML dialogs use so a ⌘⇧T
        // dark-mode user doesn't read black
        // text on a dark slate background.
        // Avalonia.Application is fully
        // qualified so the C# compiler
        // doesn't try to resolve
        // 'Application' against the
        // AIChat.Application namespace this
        // file's project is in.
        var textBrush = (IBrush?)global::Avalonia.Application.Current?.Resources["TextBrush"] ?? Brushes.Black;
        var mutedBrush = (IBrush?)global::Avalonia.Application.Current?.Resources["MutedBrush"] ?? Brushes.Gray;

        var stack = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "输入要抓取的网页 URL（http / https）",
                    FontSize = 13,
                    Foreground = textBrush,
                },
                _textBox,
                new TextBlock
                {
                    Text = "网页内容会作为数据源保存,Agent 在后续对话中可引用。",
                    FontSize = 11,
                    Foreground = mutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };
        Content = stack;

        // Close on Escape (in case the textbox
        // doesn't have focus when Escape is
        // pressed). KeyBindings on the Window
        // themselves, since the text box's own
        // KeyDown only fires when it has focus.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(Cancel),
        });
    }

    public Task<string?> ShowAsync()
    {
        // Defer ShowDialog so the caller can wire up
        // the result handler before the window
        // appears. The dispatching is handled by
        // ShowDialog itself; we just need to make
        // sure the focus lands on the text box once
        // the dialog opens.
        Dispatcher.UIThread.Post(() => _textBox.Focus(), DispatcherPriority.Background);
        return _tcs.Task;
    }

    private void Confirm()
    {
        var text = _textBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            // Empty submit: keep the dialog open so
            // the user can correct. Same affordance
            // as the "OK" / "Cancel" pair on every
            // other modal in the app.
            return;
        }
        _tcs.TrySetResult(text);
        Close();
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        Close();
    }
}
