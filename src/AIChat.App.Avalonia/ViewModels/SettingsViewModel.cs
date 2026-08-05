using System.Collections.ObjectModel;
using System.ComponentModel;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the user-facing settings surface — every field in this VM
// is the XAML-friendly mirror of an AppSettings schema field, plus
// the per-tool permission matrix. Extracted from MainWindowViewModel
// during the 1.0 refactor so the host VM can stop carrying 50+
// fields and the settings modal has a single object to bind to.
//
// Pattern: every [ObservableProperty] has a corresponding OnXxxChanged
// partial that writes through to ISettingsHolder.Current and fires
// a fire-and-forget save. The skip-if-same-value guard in each partial
// avoids redundant saves on the initial load (Refresh seeds the
// mirrors by reading from the just-normalized AppSettings, and writing
// the same value back is pure churn).
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsHolder _settingsHolder;
    private readonly IAppRepository _repository;
    private readonly AgentToolRegistry _toolRegistry;

    private AppSettings _settings => _settingsHolder.Current;

    // ---- Safety toggles (also bound from the page-header pill) ----

    [ObservableProperty]
    private bool autoVerify;

    partial void OnAutoVerifyChanged(bool value)
    {
        if (_settings.AutoVerifyAgentRuns == value)
        {
            return;
        }
        _settings.AutoVerifyAgentRuns = value;
        SaveFireAndForget();
    }

    // ---- Generation parameters ----

    [ObservableProperty]
    private double temperature;

    partial void OnTemperatureChanged(double value)
    {
        if (_settings.Temperature == value)
        {
            return;
        }
        _settings.Temperature = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private int maxOutputTokens;

    partial void OnMaxOutputTokensChanged(int value)
    {
        // 2026-08-05: clamp the value at the
        // write site. The same clamp lives
        // in MainWindowViewModel ctor (the
        // defensive belt-and-suspenders
        // pass) but clamping here means
        // the TextBox always shows the
        // corrected value immediately
        // after a typo. Without the
        // local clamp, a user who types
        // 100000 sees the TextBox show
        // 100000, the request payload go
        // out with 100000, the platform
        // 422, and then on next save
        // MainWindowViewModel ctor
        // finally corrects the value.
        // Clamping here collapses the
        // feedback loop to one frame.
        var clamped = Math.Clamp(value, 256, 16384);
        if (clamped != value)
        {
            // Push the corrected value back
            // to the TextBox without
            // re-entering this setter (the
            // early-out below catches the
            // round-trip).
            MaxOutputTokens = clamped;
            return;
        }
        if (_settings.MaxOutputTokens == value)
        {
            return;
        }
        _settings.MaxOutputTokens = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private int retryMaxAttempts;

    partial void OnRetryMaxAttemptsChanged(int value)
    {
        if (_settings.RetryMaxAttempts == value)
        {
            return;
        }
        _settings.RetryMaxAttempts = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private bool useTokenizerEstimation;

    partial void OnUseTokenizerEstimationChanged(bool value)
    {
        if (_settings.UseTokenizerEstimation == value)
        {
            return;
        }
        _settings.UseTokenizerEstimation = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private int maxAutoFixRounds;

    [ObservableProperty]
    private ThemePreference themePreference = ThemePreference.System;

    partial void OnThemePreferenceChanged(ThemePreference value)
    {
        if (_settings.ThemePreference == value)
        {
            return;
        }
        _settings.ThemePreference = value;
        SaveFireAndForget();
    }

    // Display list for the theme ComboBox. Static so the
    // ItemsControl doesn't churn. The Codex parity item is
    // just "系统 / 浅色 / 深色" — anything fancier (custom
    // accent colors) lands in a follow-up slice.
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "跟随系统"),
        new(ThemePreference.Light, "浅色"),
        new(ThemePreference.Dark, "深色"),
    ];

    partial void OnMaxAutoFixRoundsChanged(int value)
    {
        if (_settings.MaxAutoFixRounds == value)
        {
            return;
        }
        _settings.MaxAutoFixRounds = value;
        SaveFireAndForget();
    }

    // ---- Sprint 0.5: Codex-aligned 2-toggle permission model ----
    //
    // DefaultAccess and FullAccessEnabled compose into 4 effective
    // states (both off / default on only / both on / default on +
    // full off). MainWindowViewModel mirrors DefaultAccess onto
    // NoWriteMode so the existing ⌘⇧R shortcut still drives the
    // toggle. Writing _settings.DefaultAccess here triggers
    // OnDefaultAccessChanged in MainWindowViewModel which cascades
    // the badge text + NoWriteMode back; we don't write NoWriteMode
    // ourselves to avoid the partial-method echo.

    [ObservableProperty]
    private bool defaultAccess = true;

    partial void OnDefaultAccessChanged(bool value)
    {
        if (_settings.DefaultAccess == value)
        {
            return;
        }
        _settings.DefaultAccess = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private bool fullAccessEnabled;

    partial void OnFullAccessEnabledChanged(bool value)
    {
        if (_settings.FullAccessEnabled == value)
        {
            return;
        }
        _settings.FullAccessEnabled = value;
        SaveFireAndForget();
    }

    [ObservableProperty]
    private bool environmentPanelOpen = true;

    partial void OnEnvironmentPanelOpenChanged(bool value)
    {
        if (_settings.EnvironmentPanelOpen == value)
        {
            return;
        }
        _settings.EnvironmentPanelOpen = value;
        SaveFireAndForget();
    }

    // ---- Agent execution mode (preset selector) ----

    [ObservableProperty]
    private AgentExecutionMode agentExecutionMode = AgentExecutionMode.Standard;

    partial void OnAgentExecutionModeChanged(AgentExecutionMode value)
    {
        if (_settings.AgentExecutionMode == value)
        {
            return;
        }
        // The mode is a preset: applying it cascades into
        // MaxAutoFixRounds + AutoVerify + the four AgentAdaptive*
        // booleans via AgentExecutionModePolicy.Apply. Write the
        // cascade first, then mirror the cascade back so the
        // XAML re-renders the dependent fields. The mirror
        // partials' skip-if-same-value guard prevents a save
        // storm during the cascade (one extra save for the mode
        // change, no per-field redundant saves).
        AgentExecutionModePolicy.Apply(_settings, value);
        MaxAutoFixRounds = _settings.MaxAutoFixRounds;
        AutoVerify = _settings.AutoVerifyAgentRuns;
        SaveFireAndForget();
    }

    // ---- Tool permission matrix ----

    public ObservableCollection<ToolSettingViewModel> Tools { get; } = [];

    // Display lists for the two ComboBoxes. Static so the
    // ItemsSource binding doesn't churn on every refresh.
    public IReadOnlyList<AgentExecutionModeOption> AgentExecutionModeOptions { get; } =
    [
        new(AgentExecutionMode.Standard, "标准 — 默认单 Agent 循环"),
        new(AgentExecutionMode.Fast, "快速 — 小改动不开规划"),
        new(AgentExecutionMode.Deep, "深度 — 开规划 + 自动验证"),
    ];

    public IReadOnlyList<ToolModeOption> ToolModeOptions { get; } = ToolSettingViewModel.AllModes;

    public SettingsViewModel(
        ISettingsHolder settingsHolder,
        IAppRepository repository,
        AgentToolRegistry toolRegistry)
    {
        _settingsHolder = settingsHolder;
        _repository = repository;
        _toolRegistry = toolRegistry;
    }

    // ---- Wave 10: 4-category navigation + search ----

    // The left rail binds to this. Static so the ItemsControl
    // doesn't churn; the user picks one and CurrentCategory
    // flips to the corresponding value.
    public IReadOnlyList<SettingsCategoryOption> Categories { get; } =
    [
        new(SettingsCategory.Personal, "个人", "生成参数 / 主题 / 执行模式"),
        new(SettingsCategory.Integrations, "集成", "模型提供方 / 插件"),
        new(SettingsCategory.Coding, "编码", "安全策略 / 工具权限 / 修复轮数"),
        new(SettingsCategory.Archived, "已归档", "已归档的会话(暂未实现)"),
    ];

    [RelayCommand]
    private void ShowCategory(string? category)
    {
        if (Enum.TryParse<SettingsCategory>(category, out var parsed))
        {
            CurrentCategory = parsed;
        }
    }

    // Search box at the top of the modal. When non-empty,
    // the XAML shows every section across all categories
    // that matches the search text — so the user can
    // "find the Temperature setting" without first picking
    // the right category. The debounce is intentionally
    // trivial: Avalonia's TextBox fires PropertyChanged
    // on every keystroke, and the filter is a single
    // pass over < 20 strings. 500ms SLA from plan §7
    // Wave 10 is trivially met.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonalSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsIntegrationsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsCodingSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsArchivedSectionVisible))]
    private string searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonalSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsIntegrationsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsCodingSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsArchivedSectionVisible))]
    private SettingsCategory currentCategory = SettingsCategory.Personal;

    // Section visibility properties — one per category.
    // The XAML binds IsVisible to these instead of method
    // calls (Avalonia's method-binding is too fragile to
    // route through the Settings property on the host VM).
    // Search override: any non-whitespace needle that
    // matches the section's keyword set shows the section
    // regardless of which category is selected.
    private static readonly string[] PersonalKeywords =
    [
        "生成参数", "执行模式", "主题", "外观",
        "Temperature", "MaxOutputTokens", "Retry",
        "standard", "fast", "deep",
        "标准", "快速", "深度",
        "system", "light", "dark",
        "系统", "浅色", "深色"
    ];
    private static readonly string[] IntegrationsKeywords =
    [
        "模型", "提供方", "插件",
        "provider", "api", "key", "base", "url",
        "plugin", "plugin.json",
        "可用"
    ];
    private static readonly string[] CodingKeywords =
    [
        "安全", "只读", "工具", "权限",
        "自动修复", "自动验证",
        "no-write", "no write", "auto-verify", "auto-fix",
        "tool", "permission", "preset",
        "只读自动", "全部确认", "恢复默认"
    ];
    private static readonly string[] ArchivedKeywords =
    [
        "归档", "archive", "已归档"
    ];

    public bool IsPersonalSectionVisible =>
        IsSectionVisible(SettingsCategory.Personal, PersonalKeywords);

    public bool IsIntegrationsSectionVisible =>
        IsSectionVisible(SettingsCategory.Integrations, IntegrationsKeywords);

    public bool IsCodingSectionVisible =>
        IsSectionVisible(SettingsCategory.Coding, CodingKeywords);

    public bool IsArchivedSectionVisible =>
        IsSectionVisible(SettingsCategory.Archived, ArchivedKeywords);

    private bool IsSectionVisible(SettingsCategory sectionCategory, IReadOnlyList<string> keywords)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            foreach (var term in keywords)
            {
                if (term.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        return sectionCategory == CurrentCategory;
    }

    // Refresh is called by the host's RefreshAsync after settings
    // load + normalization. Seeds all mirrors from the just-loaded
    // AppSettings and rebuilds the per-tool rows. Each mirror
    // partial's skip-if-same-value guard keeps the load-time
    // assignments from firing a save.
    public void Refresh()
    {
        AutoVerify = _settings.AutoVerifyAgentRuns;
        Temperature = _settings.Temperature;
        MaxOutputTokens = _settings.MaxOutputTokens;
        RetryMaxAttempts = _settings.RetryMaxAttempts;
        UseTokenizerEstimation = _settings.UseTokenizerEstimation;
        MaxAutoFixRounds = _settings.MaxAutoFixRounds;
        AgentExecutionMode = _settings.AgentExecutionMode;
        ThemePreference = _settings.ThemePreference;
        // Sprint 0.5: pull the 2-toggle permission model + the
        // Environment panel visibility from the just-loaded settings
        // so the modal's UI matches the user's last state when they
        // re-open the settings sheet.
        DefaultAccess = _settings.DefaultAccess;
        FullAccessEnabled = _settings.FullAccessEnabled;
        EnvironmentPanelOpen = _settings.EnvironmentPanelOpen;

        Tools.Clear();
        foreach (var (tool, metadata) in _toolRegistry.AllWithMetadata())
        {
            var effective = _settings.ToolPermissionModes.TryGetValue(
                metadata.ToolId, out var configured) ? configured : metadata.DefaultPermissionMode;
            var row = new ToolSettingViewModel(
                metadata.ToolId,
                tool.Definition.Name,
                metadata.Category,
                metadata.DefaultPermissionMode)
            {
                SelectedMode = effective
            };
            row.PropertyChanged += OnToolRowPropertyChanged;
            Tools.Add(row);
        }
    }

    // ---- Tool permission presets ----
    // 15 tools in the default registry; clicking each dropdown
    // individually is friction. These three commands set every
    // tool's SelectedMode in one shot — each assignment flows
    // through OnToolRowPropertyChanged which writes
    // _settings.ToolPermissionModes + EnabledToolIds + saves.
    //
    // - "只读自动" → ReadOnly tools: AutoReadOnly; write tools:
    //   ConfirmEachTime. The default for most users.
    // - "全部确认" → every tool: ConfirmEachTime. Maximum safety.
    // - "恢复默认" → drop every ToolPermissionModes override
    //   (the row UI then re-renders DefaultPermissionMode via
    //   Refresh's TryGetValue fall-through).
    [RelayCommand]
    private void ApplyReadOnlyAutoPreset()
    {
        foreach (var row in Tools)
        {
            row.SelectedMode = row.DefaultMode == ToolPermissionMode.AutoReadOnly
                ? ToolPermissionMode.AutoReadOnly
                : ToolPermissionMode.ConfirmEachTime;
        }
    }

    [RelayCommand]
    private void ApplyConfirmAllPreset()
    {
        foreach (var row in Tools)
        {
            row.SelectedMode = ToolPermissionMode.ConfirmEachTime;
        }
    }

    [RelayCommand]
    private void ApplyDefaultPreset()
    {
        // Reset both halves of the tool policy. EnabledToolIds is the
        // availability boundary, while an absent permission entry falls back
        // to the registry default.
        _settings.ToolPermissionModes.Clear();
        _settings.EnabledToolIds = _toolRegistry.All.Select(tool => tool.Id).ToList();
        SaveFireAndForget();
        Refresh();
    }

    // Per-row PropertyChanged → host settings. Only SelectedMode
    // changes need to flow back; the other fields are immutable
    // after construction. The handler writes through to both
    // _settings.ToolPermissionModes AND _settings.EnabledToolIds
    // — Disabled removes the tool from the enabled list so the
    // model never sees it in the system prompt's AllowedTools
    // (without this it would still be advertised and the tool
    // gate would have to reject it at call time, which is a
    // no-op but wastes a model turn).
    private void OnToolRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolSettingViewModel.SelectedMode))
        {
            return;
        }
        if (sender is not ToolSettingViewModel row)
        {
            return;
        }

        if (row.SelectedMode == ToolPermissionMode.Disabled)
        {
            // Preserve Disabled explicitly so an empty EnabledToolIds list can
            // mean "the user disabled everything" instead of "fresh settings".
            _settings.ToolPermissionModes[row.Id] = ToolPermissionMode.Disabled;
            _settings.EnabledToolIds.RemoveAll(id =>
                string.Equals(id, row.Id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _settings.ToolPermissionModes[row.Id] = row.SelectedMode;
            // Disabled is the only "off" state — every other
            // mode is "on but with a different gate". Ensure
            // the id is in EnabledToolIds so the model's
            // AllowedTools list includes it.
            if (!_settings.EnabledToolIds.Any(id =>
                    string.Equals(id, row.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.EnabledToolIds.Add(row.Id);
            }
        }
        SaveFireAndForget();
    }

    // Shared fire-and-forget save for every per-field partial. async
    // void is unsafe in property partials (they're sync), so the
    // explicit ContinueWith observes the task and discards its
    // exception — same pattern the host used before the refactor.
    private void SaveFireAndForget()
    {
        _ = _repository.SaveSettingsAsync(_settings)
            .ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
    }
}

// Display label for the theme ComboBox. Same shape as
// AgentExecutionModeOption / ToolModeOption — a small
// record so the XAML can bind to a user-facing string
// instead of the raw enum value.
public sealed record ThemeOption(ThemePreference Mode, string Label);
