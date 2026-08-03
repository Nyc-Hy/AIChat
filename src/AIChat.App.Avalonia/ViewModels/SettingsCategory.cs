namespace AIChat.App.Avalonia.ViewModels;

// Wave 10 (parity plan §7 Wave 10): the four top-level
// settings categories the modal exposes. Mirrors the
// Codex Desktop settings sidebar (个人 / 集成 / 编码 /
// 已归档). The mapping from existing SettingsViewModel
// fields to categories is intentional:
//
//   个人 (Personal)  — generation params (Temperature /
//                       MaxOutputTokens / RetryMaxAttempts),
//                       execution mode, theme preference.
//                       AIChat substitutes "theme" for the
//                       Codex "外观" surface — both live in
//                       this same place; we only carry the
//                       field, not the full visual page.
//   集成 (Integrations) — model provider + key, plugins.
//                          AIChat currently surfaces the
//                          provider in the model-selector
//                          in the composer; the full
//                          provider-edit page is what this
//                          category hosts.
//   编码 (Coding)     — safety toggles (NoWriteMode /
//                       AutoVerify / UseTokenizerEstimation),
//                       tool-permission matrix, MaxAutoFixRounds.
//                       Codex's "环境 / 工作树" sections
//                       live here once those Waves land.
//   已归档 (Archived) — placeholder. AIChat doesn't have
//                       cloud-backed archive; landing a
//                       real "已归档的聊天" surface needs
//                       a "soft-delete" + restore flow
//                       that doesn't exist yet. The nav
//                       entry stays so the parity surface
//                       is visible; clicking it shows an
//                       empty-state hint.
public enum SettingsCategory
{
    Personal = 0,
    Integrations = 1,
    Coding = 2,
    Archived = 3,
}

// One row in the modal's left rail. Carries the display
// label + the index the rail binds to. Static list so
// the XAML's ItemsControl doesn't churn on every refresh.
public sealed class SettingsCategoryOption
{
    public SettingsCategory Category { get; }
    public string Label { get; }
    public string Description { get; }

    public SettingsCategoryOption(SettingsCategory category, string label, string description)
    {
        Category = category;
        Label = label;
        Description = description;
    }
}
