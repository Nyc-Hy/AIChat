namespace AIChat.Abstractions.Configuration;

// The user-facing theme choice. Persisted on AppSettings so the choice
// survives restarts.
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}
