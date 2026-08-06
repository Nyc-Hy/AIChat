using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using Moq;

namespace AIChat.Tests.Composition;

// PR-9 tests for the theme service. The service touches the global
// Avalonia Application, which is null in the test process, but the
// Apply / CycleToNext logic is purely stateful and safe to verify.
public class FluentThemeServiceTests
{
    [Fact]
    public void CycleToNext_RotatesThroughSystemLightDark()
    {
        var (service, _, _) = CreateService();

        service.CycleToNext();
        Assert.Equal(ThemePreference.Light, service.Current);
        service.CycleToNext();
        Assert.Equal(ThemePreference.Dark, service.Current);
        service.CycleToNext();
        Assert.Equal(ThemePreference.System, service.Current);
    }

    [Fact]
    public void Apply_UpdatesCurrent()
    {
        var (service, _, _) = CreateService();

        service.Apply(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, service.Current);
    }

    [Fact]
    public async Task Apply_PersistsPreferenceToRepository()
    {
        var (service, repository, holder) = CreateService();
        holder.Replace(new AppSettings());

        service.Apply(ThemePreference.Dark);
        // Give the fire-and-forget save a chance to complete.
        await Task.Delay(50);

        Mock.Get(repository).Verify(repo => repo.SaveSettingsAsync(
            It.Is<AppSettings>(settings => settings.ThemePreference == ThemePreference.Dark),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static (FluentThemeService service, IAppRepository repository, SettingsHolder holder) CreateService()
    {
        var repository = Mock.Of<IAppRepository>(repo =>
            repo.SaveSettingsAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var holder = new SettingsHolder();
        var service = new FluentThemeService(holder, repository);
        return (service, repository, holder);
    }
}
