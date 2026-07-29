using AIChat.Abstractions.Configuration;

namespace AIChat.App.Avalonia.Composition;

// Holds the current AppSettings instance so multiple view-models can share
// the same mutable reference. Used during PR-2 to break the constructor
// cycle between MainWindowViewModel and ProviderConfigViewModel: the
// provider VM needs to read the live settings, but the settings instance
// is owned by the main VM (which loads it from disk on startup).
//
// Both view-models inject this holder as a singleton. The main VM
// replaces the value after every load / save, and the provider VM reads
// it on demand. Because AppSettings is a reference type, mutations
// performed by either side are visible to the other without further
// plumbing.
public interface ISettingsHolder
{
    AppSettings Current { get; }
    void Replace(AppSettings settings);
}

public sealed class SettingsHolder : ISettingsHolder
{
    public AppSettings Current { get; private set; } = new();

    public void Replace(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = settings;
    }
}
