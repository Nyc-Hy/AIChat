using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Wave 9 (parity plan §7 Wave 9): Sites modal. Code-behind
// only loads the XAML; every binding lives on
// SitesViewModel.
public partial class SitesView : UserControl
{
    public SitesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
