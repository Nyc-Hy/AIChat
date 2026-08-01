using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Read-only code preview. Collapses to zero height when no
// file is selected (HasFile=false). All behavior (open, close,
// load) is driven by FilePreviewViewModel commands; no
// XAML-side handlers required.
public partial class FilePreviewView : UserControl
{
    public FilePreviewView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
