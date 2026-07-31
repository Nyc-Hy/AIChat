namespace AIChat.App.Avalonia.ViewModels;

// Read-only row in the "可用的提供方" list in the settings modal.
// The "Status" field is the only dynamic bit — "当前" for the active
// provider, "可用" otherwise. Lives at the bottom of the settings
// modal so the user can scan the full provider catalogue alongside
// the active template.
public sealed record ProviderCardViewModel(string Name, string DefaultModel, string Status);
