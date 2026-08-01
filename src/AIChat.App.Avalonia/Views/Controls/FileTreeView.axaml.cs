using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Code-behind placeholder so the XAML can be loaded. The
// tree's behavior is driven by FileTreeViewModel commands
// (SelectFile, ToggleFolder) and the TreeView's built-in
// expand/collapse for folder rows; no XAML-side glue is
// required here yet. Double-click → system open is added in
// a follow-up commit.
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
}
