using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
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
}
