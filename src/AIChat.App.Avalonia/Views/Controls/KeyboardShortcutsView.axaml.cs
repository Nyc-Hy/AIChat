using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Read-only keyboard-shortcut cheat sheet. The actual shortcut
// list is hard-coded in the XAML (it's documentation, not state),
// but the close-affordance wiring (Esc + scrim click) lives here
// so the modal follows the same pattern as SettingsView /
// MemoryEditorView / GitStatusView. MainWindowViewModel exposes
// CloseShortcutsCommand; this view only invokes it.
public partial class KeyboardShortcutsView : UserControl
{
    public KeyboardShortcutsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void KeyboardShortcutsScrim_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void KeyboardShortcutsScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        Close();
    }

    private void KeyboardShortcutsContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void Close()
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.CloseShortcutsCommand.CanExecute(null))
        {
            viewModel.CloseShortcutsCommand.Execute(null);
        }
    }
}
