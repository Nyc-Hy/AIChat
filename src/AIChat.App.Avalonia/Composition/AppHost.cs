using Microsoft.Extensions.DependencyInjection;

namespace AIChat.App.Avalonia.Composition;

// Builds the service provider that backs the Avalonia app. Kept deliberately
// thin: composition root only. Tests can call Build() with a customised
// IServiceCollection to override individual services (for example swapping
// IAppRepository for an in-memory fake) without touching App.axaml.cs.
//
// Public so AIChat.Tests can build and assert on the container. Kept sealed
// and dependency-free on purpose — anything that needs app-state should live
// in a service registered via ServiceRegistration.
public static class AppHost
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddAIChatDesktop();
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static ServiceProvider Build(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAIChatDesktop();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
