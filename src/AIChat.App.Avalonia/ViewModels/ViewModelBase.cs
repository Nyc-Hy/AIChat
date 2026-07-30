using global::Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    // Look up a theme-aware brush by token key. Returns Brushes.Transparent if
    // the resource can't be resolved (e.g. in unit tests where the Avalonia
    // application is not initialised). The view models use this instead of
    // hard-coded hex strings so the same brush flips between light and dark
    // when Application.RequestedThemeVariant changes.
    protected static IBrush TokenBrush(string key)
    {
        var app = global::Avalonia.Application.Current;
        if (app is null)
        {
            return Brushes.Transparent;
        }

        // Walk the resource stack honouring the current theme so dark-mode
        // overrides are picked up automatically.
        var theme = app.RequestedThemeVariant;
        if (app.Resources.TryGetResource(key, theme, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Transparent;
    }
}
