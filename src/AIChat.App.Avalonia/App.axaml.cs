using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AIChat.Abstractions.Configuration;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.Views;
using AIChat.Application.BackgroundProcesses;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.App.Avalonia;

public partial class App : global::Avalonia.Application
{
    private ServiceProvider? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 2026-08-03: register global exception hooks first so any
        // failure during DI / window construction (e.g. a misbehaving
        // AppHost.Build) is captured to crash.log instead of
        // terminating the process with no diagnostic.
        CrashReporter.Register(AppRuntimeProfile.CrashLogFile);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve the main window from the composition root. The window's
            // DataContext (the MainWindowViewModel) is injected by the DI
            // container so we don't need to wire it manually here. The
            // MainWindowViewModel kicks off an async refresh that loads
            // settings, so the theme is applied reactively once the load
            // completes — the system default is good enough for the
            // first paint.
            _host = AppHost.Build();
            var mainWindow = _host.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            // 1.0.1: start the cron engine. The
            // hosted service runs a PeriodicTimer
            // (30s tick) that drives ScheduledTaskRunner
            // — every tick scans the registry for due
            // tasks and fires them through the
            // IScheduledTaskExecutor (which routes
            // through AgentHost.SendTaskAsync). The
            // service is a singleton; we own the
            // start/stop lifecycle here so the tick
            // is gone before the DI container is
            // disposed on shutdown.
            var scheduler = _host.GetRequiredService<SchedulerHostedService>();
            scheduler.Start();
            desktop.Exit += (_, _) =>
            {
                // 2026-08-03: stop background processes (Sites previews
                // and any other supervised children) before disposing
                // the DI container. The supervisor's setpgid'd process
                // group is otherwise leaked when the host's signal
                // handlers tear down, because the kernel does not
                // guarantee orphaned process-group cleanup on
                // macOS / Linux parent death.
                if (_host is not null)
                {
                    try
                    {
                        scheduler.DisposeAsync().AsTask()
                            .GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Shutdown path: never let cleanup throw.
                    }
                    try
                    {
                        var supervisor = _host.GetService<IBackgroundProcessSupervisor>();
                        if (supervisor is not null)
                        {
                            supervisor.StopAllAsync(TimeSpan.FromSeconds(5))
                                .GetAwaiter().GetResult();
                        }
                    }
                    catch
                    {
                        // Shutdown path: never let cleanup throw.
                    }
                }
                _host?.Dispose();
                _host = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void About_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            ShowAboutWindow(mainWindow);
        }
    }

    private void Settings_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: ViewModels.MainWindowViewModel viewModel })
        {
            viewModel.OpenSettingsCommand.Execute(null);
        }
    }

    private static void ShowAboutWindow(Window owner)
    {
        var about = new Window
        {
            Title = "关于 AIChat",
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "AIChat", FontSize = 24, FontWeight = global::Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = "面向日常编码工作的本地 Coding Agent。", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = "版本 1.0.0", Opacity = 0.65 },
                }
            }
        };
        _ = about.ShowDialog(owner);
    }
}
