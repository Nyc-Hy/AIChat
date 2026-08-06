using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Memory editor modal (⌘⇧M). Same pattern as SettingsView —
// inherit the host's DataContext, scrim click closes, content
// click consumes the event so the modal stays open while the
// user is typing / scrolling entries.
public partial class MemoryEditorView : UserControl
{
    public MemoryEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MemoryEditorScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsMemoryEditorOpen = false;
        }
    }

    private void MemoryEditorContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
