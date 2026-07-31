using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Settings modal: extracted from MainWindow.axaml during the
// 1.0 refactor. The UserControl inherits the parent
// DataContext (MainWindowViewModel) so the inline Provider /
// Settings / NoWriteMode bindings keep resolving — no view-
// model surgery required. IsVisible is bound on the UserControl
// element from the host (MainWindowViewModel.IsSettingsOpen);
// the inner Border is the scrim + content, the same shape as
// the inline modal had.
//
// Scrim click closes the modal; content click consumes the
// event so the modal doesn't close when the user clicks inside
// the dialog.
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SettingsScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsSettingsOpen = false;
        }
    }

    private void SettingsContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
