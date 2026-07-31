namespace AIChat.App.Avalonia.ViewModels;

// One row in the provider-template dropdown. Carries the catalogue
// defaults (base URL + model) so the settings modal can prefill
// the input fields when the user picks a template that isn't
// currently active. The dropdown is a template picker, not a
// provider list — the latter is the "可用的提供方" list (see
// ProviderCardViewModel) rendered below.
public sealed record ProviderTemplateViewModel(string Id, string Name, string DefaultBaseUrl, string DefaultModel);
