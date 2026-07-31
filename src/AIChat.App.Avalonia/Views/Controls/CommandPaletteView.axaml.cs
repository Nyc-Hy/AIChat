using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Command palette (⌘K) modal. Extracted from MainWindow.axaml
// during the 1.0 refactor. The UserControl inherits the parent
// DataContext (MainWindowViewModel) so the CommandPalette.* +
// IsCommandPaletteOpen bindings keep resolving. The view code-
// behind handles KeyDown (Enter / Arrow / Esc) and the scrim
// click (close). The search input is named so code-behind can
// focus it on open.
public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Called by the host (MainWindow.axaml.cs) every time the
    // palette opens so the user can start typing immediately.
    // The named TextBox is in the XAML; FindControl is the
    // canonical way to reach a named element from code-behind
    // when the field isn't auto-generated (AVLN3001 warning
    // in precompiled XAML catches missing public ctor on the
    // partial class — the standard pattern is FindControl).
    public void FocusSearchInput()
    {
        var input = this.FindControl<TextBox>("CommandSearchInput");
        input?.Focus();
    }

    private void CommandPalette_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            // Fire-and-forget the async execute. The palette closes
            // on success (the action sets IsCommandPaletteOpen=false
            // through the host's OpenCommandPaletteCommand). The
            // SafeRun pattern is for event handlers, not palette
            // key handlers — the execute swallows its own errors
            // and the user's next key press dismisses the palette
            // even if the action failed.
            _ = viewModel.CommandPalette.ExecuteSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            viewModel.CommandPalette.SelectedIndex++;
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            viewModel.CommandPalette.SelectedIndex--;
            e.Handled = true;
        }
    }

    private void CommandPaletteScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsCommandPaletteOpen = false;
        }
    }

    private void CommandPaletteContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
