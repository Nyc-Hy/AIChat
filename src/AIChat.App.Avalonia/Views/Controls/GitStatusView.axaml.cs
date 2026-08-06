using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Git status / diff viewer modal (⌘⇧G). Two-column body: file
// list on the left, read-only diff viewer on the right. Same
// scrim + content click pattern as the other modals.
public partial class GitStatusView : UserControl
{
    public GitStatusView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void GitStatusScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsGitStatusOpen = false;
        }
    }

    private void GitStatusContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
