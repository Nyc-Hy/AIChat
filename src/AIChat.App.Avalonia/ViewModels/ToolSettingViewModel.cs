using System.Collections.Generic;
using AIChat.Abstractions.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using AIChat.Application.Agents;

namespace AIChat.App.Avalonia.ViewModels;

// Per-row view-model for the "工具权限" matrix in the settings
// modal. The XAML renders one row per registered tool, with a
// ComboBox bound to SelectedMode for the per-tool permission
// override. SettingsViewModel writes SelectedMode through to the host's
// _settings.ToolPermissionModes dictionary. Disabled is persisted explicitly
// so an empty EnabledToolIds list is not mistaken for first-run defaults.
public sealed partial class ToolSettingViewModel : ViewModelBase
{
    [ObservableProperty]
    private string id = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string category = "";

    [ObservableProperty]
    private ToolPermissionMode selectedMode;

    [ObservableProperty]
    private ToolPermissionMode defaultMode;

    // Display strings for the ComboBox items. Static list per
    // process so the ItemsSource binding doesn't churn. The XAML
    // uses SelectedMode + these labels via DisplayMemberPath +
    // SelectedValuePath (or a converter) — the source-of-truth is
    // the enum, the display is the friendly label.
    public static IReadOnlyList<ToolModeOption> AllModes { get; } =
    [
        new(ToolPermissionMode.Disabled, "禁用"),
        new(ToolPermissionMode.AutoReadOnly, "自动放行（只读）"),
        new(ToolPermissionMode.ConfirmEachTime, "每次确认"),
        new(ToolPermissionMode.AllowForSession, "本会话放行"),
    ];

    public ToolSettingViewModel(string id, string displayName, string category, ToolPermissionMode defaultMode)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        DefaultMode = defaultMode;
        SelectedMode = defaultMode;
    }
}

public sealed record ToolModeOption(ToolPermissionMode Mode, string Label);

// Display record for the "执行模式" ComboBox. Mirrors
// ToolModeOption's shape (Mode + Label) so the XAML can use the
// same ItemTemplate pattern for both dropdowns.
public sealed record AgentExecutionModeOption(AgentExecutionMode Mode, string Label);
