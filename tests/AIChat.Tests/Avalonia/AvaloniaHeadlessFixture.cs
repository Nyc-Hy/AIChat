using Avalonia;
using Avalonia.Headless;

namespace AIChat.Tests.Avalonia;

// PR-10: headless test fixture that initialises the Avalonia platform so
// the test process can run view-model and dispatcher-dependent code without
// a display server. The fixture is a no-op once Avalonia is up; the
// [AvaloniaTestApplication] attribute would do the same via xunit v3 but
// we are still on xunit v2.
//
// Tests that touch Dispatcher.UIThread.Post must wrap their assertions in
// Dispatcher.UIThread.InvokeAsync(...) to run them on the UI thread. The
// fixture only ensures the platform is initialised; it does not pump
// messages.
public sealed class AvaloniaHeadlessFixture : IDisposable
{
    public AvaloniaHeadlessFixture()
    {
        var app = AppBuilder.Configure<AvaloniaHeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .SetupWithoutStarting();
        _ = app; // configuration is enough; the platform is now live
    }

    public void Dispose()
    {
        // No teardown — the Avalonia headless platform is process-wide.
    }

    // Headless app that satisfies Avalonia's expectation of a concrete
    // Application subclass. It does nothing beyond the framework's
    // default behaviour.
    private sealed class AvaloniaHeadlessApp : global::Avalonia.Application
    {
    }
}
