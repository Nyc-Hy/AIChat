using AIChat.Abstractions.Configuration;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Tools;
using AIChat.Tests.TestDoubles;
using Moq;

namespace AIChat.Tests.Avalonia;

// Wave 10 (parity plan §7 Wave 10): pin the new
// SettingsViewModel category navigation + search filter
// contract. The XAML binds `IsVisible` to these bool
// properties; a regression here means settings sections
// show up in the wrong category or get hidden when they
// should match the search.
public sealed class SettingsViewModelCategoryTests
{
    [Fact]
    public void Ctor_CategoriesListExposesFourEntries()
    {
        var vm = CreateViewModel();

        Assert.Equal(4, vm.Categories.Count);
        Assert.Equal(SettingsCategory.Personal, vm.Categories[0].Category);
        Assert.Equal(SettingsCategory.Integrations, vm.Categories[1].Category);
        Assert.Equal(SettingsCategory.Coding, vm.Categories[2].Category);
        Assert.Equal(SettingsCategory.Archived, vm.Categories[3].Category);
    }

    [Fact]
    public void Ctor_DefaultsToPersonalCategory()
    {
        var vm = CreateViewModel();

        Assert.Equal(SettingsCategory.Personal, vm.CurrentCategory);
        Assert.True(vm.IsPersonalSectionVisible);
        Assert.False(vm.IsIntegrationsSectionVisible);
        Assert.False(vm.IsCodingSectionVisible);
        Assert.False(vm.IsArchivedSectionVisible);
    }

    [Fact]
    public void ShowCategoryCommand_SwitchesActiveCategory()
    {
        var vm = CreateViewModel();

        vm.ShowCategoryCommand.Execute("Integrations");

        Assert.Equal(SettingsCategory.Integrations, vm.CurrentCategory);
        Assert.False(vm.IsPersonalSectionVisible);
        Assert.True(vm.IsIntegrationsSectionVisible);
    }

    [Fact]
    public void ShowCategoryCommand_InvalidValue_NoOps()
    {
        var vm = CreateViewModel();

        vm.ShowCategoryCommand.Execute("NotARealCategory");

        // The default category is unchanged when the
        // parser doesn't recognise the string.
        Assert.Equal(SettingsCategory.Personal, vm.CurrentCategory);
    }

    [Fact]
    public void SearchText_OverridesCategoryFilter()
    {
        // A non-empty search needle that matches any
        // keyword in the Integrations section reveals
        // that section even when the user is currently
        // on the Personal category. The plan §7 Wave 10
        // "search 500ms SLA + directly locate setting"
        // rule depends on this cross-category filter.
        var vm = CreateViewModel();
        vm.ShowCategoryCommand.Execute("Personal");
        Assert.True(vm.IsPersonalSectionVisible);
        Assert.False(vm.IsIntegrationsSectionVisible);

        vm.SearchText = "模型";

        Assert.True(vm.IsIntegrationsSectionVisible);
        // Personal keywords don't include "模型", so the
        // Personal section collapses when search is on.
        Assert.False(vm.IsPersonalSectionVisible);
    }

    [Fact]
    public void SearchText_EmptyFallsBackToCategoryFilter()
    {
        var vm = CreateViewModel();
        vm.ShowCategoryCommand.Execute("Coding");
        vm.SearchText = "tool";
        Assert.True(vm.IsCodingSectionVisible);

        // Clearing the search reverts to the category filter.
        vm.SearchText = "";

        Assert.True(vm.IsCodingSectionVisible);
        Assert.False(vm.IsPersonalSectionVisible);
    }

    [Fact]
    public void SearchText_NoMatchHidesAllSections()
    {
        var vm = CreateViewModel();
        vm.SearchText = "zzzz-nothing-matches-this";

        Assert.False(vm.IsPersonalSectionVisible);
        Assert.False(vm.IsIntegrationsSectionVisible);
        Assert.False(vm.IsCodingSectionVisible);
        Assert.False(vm.IsArchivedSectionVisible);
    }

    [Fact]
    public void SearchText_IsCaseInsensitive()
    {
        var vm = CreateViewModel();

        vm.SearchText = "TEMPERATURE";

        Assert.True(vm.IsPersonalSectionVisible);
    }

    [Fact]
    public void Refresh_LoadsThemePreferenceFromSettings()
    {
        // Lock the Refresh -> AppSettings round-trip so
        // the new theme field doesn't drift between
        // schema and UI. (Other fields are pinned by
        // existing tests; this one is new in Wave 10.)
        var settings = new AppSettings { ThemePreference = ThemePreference.Dark };
        var vm = CreateViewModel(settings);
        vm.Refresh();

        Assert.Equal(ThemePreference.Dark, vm.ThemePreference);
    }

    [Fact]
    public void OnThemePreferenceChanged_WritesThroughToSettings()
    {
        var settings = new AppSettings { ThemePreference = ThemePreference.System };
        var vm = CreateViewModel(settings);
        vm.Refresh();

        vm.ThemePreference = ThemePreference.Light;

        Assert.Equal(ThemePreference.Light, settings.ThemePreference);
    }

    [Fact]
    public void ThemeOptions_ExposesThreeEntries()
    {
        var vm = CreateViewModel();

        Assert.Equal(3, vm.ThemeOptions.Count);
        Assert.Contains(vm.ThemeOptions, option => option.Mode == ThemePreference.System);
        Assert.Contains(vm.ThemeOptions, option => option.Mode == ThemePreference.Light);
        Assert.Contains(vm.ThemeOptions, option => option.Mode == ThemePreference.Dark);
    }

    private static SettingsViewModel CreateViewModel(AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        var holder = new SettingsHolder();
        holder.Replace(settings);
        var repository = new InMemoryAppRepository();
        var registry = AgentToolRegistry.CreateForTests([]);
        return new SettingsViewModel(holder, repository, registry);
    }
}
