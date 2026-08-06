using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

public partial class RunHistoryView : UserControl
{
    public RunHistoryView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void RunHistoryScrim_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsRunHistoryOpen = false;
        }
    }

    private void RunHistoryContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // 1.0.1: "→ composer" button on the
    // Goal. Hands off to the host
    // (MainWindowViewModel.
    // CopyRunGoalToComposer) which sets
    // the composer text, raises
    // FocusComposerRequested, and closes
    // the modal in one go. The user
    // lands on the composer caret
    // positioned at the end of the
    // pasted goal, ready to edit +
    // re-send. Tag carries the goal
    // text so the click handler doesn't
    // need to walk the DataContext.
    private void CopyGoalToComposer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string goal } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        viewModel.CopyRunGoalToComposer(goal);
    }
}
