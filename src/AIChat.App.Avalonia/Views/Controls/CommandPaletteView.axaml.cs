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

    private async void CommandPalette_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ExecuteSelectedSafelyAsync(viewModel);
        }
        else if (e.Key == Key.Down)
        {
            viewModel.CommandPalette.MoveNextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            viewModel.CommandPalette.MovePreviousCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void CommandPaletteItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: CommandItem item })
        {
            return;
        }

        e.Handled = true;
        var index = viewModel.CommandPalette.FilteredCommands.IndexOf(item);
        if (index >= 0)
        {
            viewModel.CommandPalette.SelectedIndex = index;
        }
        await ExecuteSelectedSafelyAsync(viewModel);
    }

    private static async Task ExecuteSelectedSafelyAsync(MainWindowViewModel viewModel)
    {
        try
        {
            if (await viewModel.CommandPalette.ExecuteSelectedAsync())
            {
                viewModel.IsCommandPaletteOpen = false;
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"操作失败：{ex.Message}";
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
