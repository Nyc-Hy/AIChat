using System.Collections.ObjectModel;
using System.ComponentModel;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using CommunityToolkit.Mvvm.ComponentModel;

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

    partial void OnMaxAutoFixRoundsChanged(int value)
    {
        if (_settings.MaxAutoFixRounds == value)
        {
            return;
        }
        _settings.MaxAutoFixRounds = value;
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
            // Disabled is the absence semantic. Drop the
            // ToolPermissionModes entry (the absence is the
            // signal; a stored Disabled value would just be
            // redundant) and remove the tool from EnabledToolIds
            // so the system prompt's tool list doesn't include
            // it. Re-enabling (picking any other mode) re-adds
            // the id to EnabledToolIds in the else branch.
            _settings.ToolPermissionModes.Remove(row.Id);
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
