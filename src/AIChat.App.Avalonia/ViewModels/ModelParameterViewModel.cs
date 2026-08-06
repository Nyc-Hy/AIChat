using AIChat.Abstractions.Llm;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// One per-model parameter (e.g. "思考模式", "top_p",
// "并行工具调用", "JSON 模式") in the Settings modal.
//
// 2026-08-04: the catalog (ChatProviderCatalog) defines
// what knobs each model exposes; this VM renders one
// row per knob as a labeled ComboBox. The Options
// collection is the catalog's Options list (init-only
// on LlmModelParameterInfo, so the VM is a thin
// reference wrapper, not a copy). SelectedValue is the
// string the user picked from the dropdown — the empty
// string means "默认" (don't send anything on the wire,
// let the platform pick its default).
//
// The parent (ProviderConfigViewModel) repopulates its
// ModelParameters collection when the user switches
// provider or model. The empty-string value
// intentionally round-trips through settings as "" so
// `settings.ModelParameters["minimax.thinking"] = ""`
// means "no override" — the
// OpenAICompatibleChatProvider switch's
// `string.IsNullOrWhiteSpace(parameter.Value) continue`
// skips emitting the field on the wire (the platform
// default applies).
public sealed partial class ModelParameterViewModel : ObservableObject
{
    public LlmModelParameterInfo Parameter { get; }

    [ObservableProperty]
    private string selectedValue = "";

    public ModelParameterViewModel(LlmModelParameterInfo parameter, string initialValue = "")
    {
        Parameter = parameter;
        // The catalog's options list is the source of
        // truth for what's pickable; the VM holds the
        // current value as a string. The Options
        // accessor is wrapped in a property to satisfy
        // the XAML's expectation of a stable, bindable
        // collection — but the underlying list is
        // shared across all VMs (it's read-only).
        SelectedValue = initialValue ?? "";
    }

    public IReadOnlyList<LlmParameterOption> Options => Parameter.Options;
}
