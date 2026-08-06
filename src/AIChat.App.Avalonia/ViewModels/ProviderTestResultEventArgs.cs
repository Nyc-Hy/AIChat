using AIChat.Abstractions.Configuration;

namespace AIChat.App.Avalonia.ViewModels;

// Event payload raised by ProviderConfigViewModel after a save attempt.
// The parent (MainWindowViewModel) uses it to refresh active-provider
// display state and to surface the result in the status line.
public sealed class ProviderSavedEventArgs : EventArgs
{
    public required AppSettings Settings { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelId { get; init; }
    public required bool AlreadyExisted { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WarningMessage { get; init; }
}

// Event payload raised by ProviderConfigViewModel before a connection test
// runs. The parent uses it to add a "running" activity entry and update
// the status line.
public sealed class ProviderTestStartedEventArgs : EventArgs
{
    public required string ProviderName { get; init; }
    public required string ModelId { get; init; }
}

// Event payload raised by ProviderConfigViewModel after a connection test
// attempt. IsSuccess=false covers both validation failures and transport
// failures; the message discriminates between them.
public sealed class ProviderTestCompletedEventArgs : EventArgs
{
    public required bool IsSuccess { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}
