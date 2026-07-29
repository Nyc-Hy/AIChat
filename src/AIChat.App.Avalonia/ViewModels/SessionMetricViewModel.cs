namespace AIChat.App.Avalonia.ViewModels;

// One cell in the right-rail "session metrics" strip.
public sealed record SessionMetricViewModel(string Label, string Value, string Tooltip);
