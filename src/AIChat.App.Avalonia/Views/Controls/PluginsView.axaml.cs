using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Wave 8 (parity plan §7 Wave 8): Plugins modal. Code-behind
// only loads the XAML; every binding lives on PluginsViewModel.
public partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
