using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace AIChat.App.Avalonia.Views.Controls;

// First-impression empty state — split out from MainWindow.axaml
// so the welcome chrome (hero + first-run CTAs + 4 quick-action
// cards) lives in its own UserControl. The host (MainWindow)
// binds Greeting / SubGreeting / HasProject / OpenSettingsCommand
// as styled properties and subscribes to the two RoutedEventArgs-
// flavoured events below to forward to its existing AddProject /
// EmptyStateCard handlers — the same code paths ⌘O / ⌘, / palette /
// direct click all converge on.
//
// DataContext: the host (MainWindow) sets DataContext to itself
// (its own MainWindowViewModel flows in as inherited DataContext
// when the EmptyStateView doesn't override it) so that the
// styled-property bindings Greeting="{Binding AppStatus.Greeting}"
// resolve against the host's VM. Inner XAML uses x:Name="Self" +
// "#Self.Greeting" to read back the styled property the host
// just set — Avalonia's binding system supports a self-reference
// path because the styled property's setter is a real CLR property
// on the control. (The previous "DataContext = this" hack made
// the styled property bindings resolve against the control itself,
// which broke the host's binding chain — the host's AppStatus path
// doesn't exist on the control, so the Greeting setter was never
// called and the hero TextBlock was permanently empty.)
public sealed partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> GreetingProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Greeting), "");

    public static readonly StyledProperty<string> SubGreetingProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(SubGreeting), "");

    public static readonly StyledProperty<bool> HasProjectProperty =
        AvaloniaProperty.Register<EmptyStateView, bool>(nameof(HasProject));

    public static readonly StyledProperty<ICommand?> OpenSettingsCommandProperty =
        AvaloniaProperty.Register<EmptyStateView, ICommand?>(nameof(OpenSettingsCommand));

    public string Greeting
    {
        get => GetValue(GreetingProperty);
        set => SetValue(GreetingProperty, value);
    }

    public string SubGreeting
    {
        get => GetValue(SubGreetingProperty);
        set => SetValue(SubGreetingProperty, value);
    }

    public bool HasProject
    {
        get => GetValue(HasProjectProperty);
        set => SetValue(HasProjectProperty, value);
    }

    public ICommand? OpenSettingsCommand
    {
        get => GetValue(OpenSettingsCommandProperty);
        set => SetValue(OpenSettingsCommandProperty, value);
    }

    public EmptyStateView()
    {
        InitializeComponent();
    }

    // Bubble up the "user clicked the Add Project card" event
    // with no payload — the host's AddProject handler opens the
    // folder picker. Exposed as a RoutedEvent so the host can
    // wire it in XAML with a single Click="..." attribute.
    public event EventHandler<RoutedEventArgs>? AddProjectRequested;

    // Bubble up the "user clicked a quick-action card" event.
    // The prompt text is the sender Button's Tag; the host's
    // EmptyStateCard handler reads it the same way it always
    // has (just now the Button lives inside this control).
    public event EventHandler<RoutedEventArgs>? QuickActionRequested;

    private void OnAddProjectClick(object? sender, RoutedEventArgs e)
    {
        AddProjectRequested?.Invoke(this, e);
    }

    private void OnQuickActionClick(object? sender, RoutedEventArgs e)
    {
        QuickActionRequested?.Invoke(this, e);
    }
}
