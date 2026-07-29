using AIChat.Abstractions.Configuration;

namespace AIChat.App.Avalonia.Composition;

// Boundary between the view layer and Avalonia's theme system. The
// service remembers the current theme, updates the live application, and
// reads/writes the persisted preference via ISettingsHolder.
public interface IThemeService
{
    ThemePreference Current { get; }
    void Apply(ThemePreference preference);
    void CycleToNext();
}
