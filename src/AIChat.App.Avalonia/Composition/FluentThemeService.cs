using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using Avalonia;
using Avalonia.Styling;

namespace AIChat.App.Avalonia.Composition;

// Default IThemeService that drives Avalonia's RequestedThemeVariant on
// the live application and persists the choice through the shared
// SettingsHolder. The holder is shared with the rest of the app so the
// theme survives the same persistence cycle as everything else.
public sealed class FluentThemeService : IThemeService
{
    private readonly ISettingsHolder _settingsHolder;
    private readonly IAppRepository _repository;
    private ThemePreference _current = ThemePreference.System;

    public FluentThemeService(ISettingsHolder settingsHolder, IAppRepository repository)
    {
        _settingsHolder = settingsHolder;
        _repository = repository;
    }

    public ThemePreference Current => _current;

    public void Apply(ThemePreference preference)
    {
        _current = preference;
        var variant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        if (global::Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }

        // Persist asynchronously; if the save fails the in-memory
        // preference still applies for this session.
        _ = PersistAsync(preference);
    }

    public void CycleToNext()
    {
        var next = _current switch
        {
            ThemePreference.System => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            ThemePreference.Dark => ThemePreference.System,
            _ => ThemePreference.System
        };
        Apply(next);
    }

    private async Task PersistAsync(ThemePreference preference)
    {
        try
        {
            _settingsHolder.Current.ThemePreference = preference;
            await _repository.SaveSettingsAsync(_settingsHolder.Current);
        }
        catch
        {
            // Persistence is best-effort; the in-memory preference stays.
        }
    }
}
