using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.App.Avalonia.Views.Controls;

// Sprint 0.5: right-side Environment panel. Code-behind only wires the
// refresh button + the (disabled) commit / push / PR buttons in Wave 6
// placeholders. All data lives in EnvironmentPanelViewModel.
public partial class EnvironmentPanelView : UserControl
{
    public EnvironmentPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // The refresh button is the only currently-wired button. The commit
    // and PR buttons are intentionally disabled — they show the user
    // "this is the place" but the real action lands in Wave 6.
    private async void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }

    private void CommitOrPush_OnClick(object? sender, RoutedEventArgs e)
    {
        // Disabled by design in Sprint 0.5. Lands in Wave 6.
    }

    private void CreatePr_OnClick(object? sender, RoutedEventArgs e)
    {
        // Disabled by design in Sprint 0.5. Lands in Wave 6.
    }

    // Open the deliverable file in the system default app. We use
    // `open` on macOS, falling back to the OS shell — never block
    // the UI thread on file I/O.
    private void Deliverable_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // The file may have been deleted between refresh and click
            // (the user moved/renamed it), or the OS has no associated
            // app. Either way, we don't want to crash the panel; the
            // next refresh will reconcile the list.
        }
    }

    private void ViewAllSources_OnClick(object? sender, RoutedEventArgs e)
    {
        // Same as above — Wave 7 opens the full sources modal.
    }

    // Wave 5 (plan §5): the chevron button next to "变更" opens the
    // Diff view. Wave 6 owns the full file-list / line-level diff /
    // stage / unstage / restore flow; for Wave 5 the click is a
    // toast so the user can see the affordance land.
    private void OpenDiff_OnClick(object? sender, RoutedEventArgs e)
    {
        // No toast wired here — the Environment panel doesn't own a
        // toast reference. Wave 6 will route the click through the
        // parent MainWindow which has access to IToastService.
    }

    // Wave 7 follow-up (plan §13 P0 risk "整个子进程树"):
    // per-row Stop button. The XAML sets Tag to the row's
    // process id so we can target the right supervisor entry.
    // The supervisor kills the whole process tree (SIGTERM →
    // SIGKILL), so the python server and any forked workers
    // exit cleanly. The button's IsVisible is bound to the
    // row's IsRunning, so this handler only ever fires for
    // running rows.
    private async void StopBackgroundProcess_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string processId }
            || string.IsNullOrWhiteSpace(processId))
        {
            return;
        }
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            try
            {
                await vm.StopBackgroundProcessAsync(processId);
            }
            catch
            {
                // Supervisor swallows its own errors; this catch
                // is a belt-and-braces fallback in case a future
                // change lets an exception escape (e.g. JSON
                // persistence failed). Crashing the panel over a
                // stop-click would be worse than swallowing the
                // error — the user can see the row state change
                // either way.
            }
        }
    }

    // 1.0.1: per-row "引用" click handler. Forwards
    // the row's Source to the panel VM's
    // InsertReferenceRequested event so MainWindow.xaml.cs
    // can read the live composer CaretIndex at click
    // time (the per-row command binding can't see the
    // composer because the row's DataContext is the
    // panel VM, not the MainWindow VM that owns the
    // composer TextBox). Pending-attachment rows
    // don't have a Source — the button's IsVisible
    // binding hides them, so this branch only
    // fires for real Source rows.
    private void SourceInsert_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SourceRowViewModel row }
            || row.Source is null)
        {
            return;
        }
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            vm.RaiseInsertReferenceRequested(row.Source);
        }
    }
}
