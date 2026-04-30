using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIChat.App.Controls;

// Small modal prompt used for renaming conversations. It is built in C# because
// the app only needs one simple reusable dialog.
public sealed class TextPromptDialog : Window
{
    private readonly TextBox _textBox;

    private TextPromptDialog(string title, string value)
    {
        Title = title;
        Width = 420;
        Height = 188;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        _textBox = new TextBox
        {
            Text = value,
            Height = 38,
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(221, 226, 234)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 37, 46))
        };

        var cancelButton = new Button
        {
            Content = "取消",
            Width = 82,
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 234, 241))
        };
        cancelButton.Click += (_, _) => DialogResult = false;

        var confirmButton = new Button
        {
            Content = "确定",
            Width = 86,
            Height = 36,
            Background = new SolidColorBrush(Color.FromRgb(37, 109, 103)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        confirmButton.Click += (_, _) => DialogResult = true;

        Content = new Border
        {
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 236)),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(16) },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(18) },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(30, 37, 46))
                    },
                    _textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelButton, confirmButton }
                    }
                }
            }
        };

        Grid.SetRow(_textBox, 2);
        // The StackPanel was created inline above; move it into the last grid row.
        Grid.SetRow(((Grid)((Border)Content).Child).Children[2], 4);

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                DialogResult = false;
            }
            else if (args.Key == Key.Enter)
            {
                DialogResult = true;
            }
        };
    }

    public static string? Show(Window? owner, string title, string value)
    {
        // Return null on cancel so callers can distinguish cancel from empty text.
        var dialog = new TextPromptDialog(title, value)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog._textBox.Text : null;
    }
}
