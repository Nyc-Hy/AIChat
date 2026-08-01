using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Windows.Input;

namespace AIChat.App.Avalonia.Views.Controls;

// First-impression empty state — split out from MainWindow.axaml
// so the welcome chrome (hero + first-run CTAs + 4 quick-action
// cards) lives in its own UserControl. The host (MainWindow)
// binds Greeting / SubGreeting / HasProject / OpenSettingsCommand
// as styled properties (this control's own DataContext is
// `this`, so the XAML's {Binding ...} resolves to them) and
// subscribes to the two RoutedEventArgs-flavoured events below
// to forward to its existing AddProject / EmptyStateCard
// handlers — the same code paths ⌘O / ⌘, / palette / direct
// click all converge on.
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
        // The internal XAML's {Binding Greeting} etc. resolves to
        // this control's own styled properties (rather than
        // MainWindowViewModel) so the user control is self-
        // contained and the host can wire it up in one block.
        DataContext = this;
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
