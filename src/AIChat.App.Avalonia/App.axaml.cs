using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.Views;
using AIChat.Abstractions.Persistence;
using AIChat.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.App.Avalonia;

public partial class App : global::Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve the main window from the composition root. The window's
            // DataContext (the MainWindowViewModel) is injected by the DI
            // container so we don't need to wire it manually here. The
            // MainWindowViewModel kicks off an async refresh that loads
            // settings, so the theme is applied reactively once the load
            // completes — the system default is good enough for the
            // first paint.
            var host = AppHost.Build();
            desktop.MainWindow = host.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
