using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.App.Avalonia.Views.Controls;

// Code-behind: only the double-click affordance lives here.
// Single-click selection stays in XAML (Command binding on the
// row's Button), so the VM doesn't see click noise. Double-click
// is a separate gesture that resolves to "I want to edit this
// in my real IDE", not "I want to look at it in the preview".
public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void FileRow_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Button { DataContext: FileTreeNodeViewModel node } button)
        {
            return;
        }
        // Walk up to the UserControl's DataContext (= the
        // FileTreeViewModel) so we can invoke its command. The
        // XAML binding on the Button itself only fires for the
        // single-click "select" command.
        if (button.FindAncestorOfType<UserControl>() is { DataContext: FileTreeViewModel vm })
        {
            vm.OpenWithSystemAppCommand.Execute(node);
        }
    }
}
