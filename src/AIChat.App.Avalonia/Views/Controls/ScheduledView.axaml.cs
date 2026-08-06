using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIChat.App.Avalonia.Views.Controls;

// Wave 9 (parity plan §7 Wave 9): Scheduled modal. Code-
// behind only loads the XAML; every binding lives on
// ScheduledViewModel.
public partial class ScheduledView : UserControl
{
    public ScheduledView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
