using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
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

    // Refresh button: re-reads the upstream
    // state (git changes via
    // WorkspaceChangeService, sub-agent
    // runs from AgentHost, source
    // registry, background processes).
    // The other two buttons (提交或推送
    // / 创建拉取请求) are wired — the
    // first opens the Git status modal
    // (b7ccaec), the second stays
    // disabled pending GitHub OAuth
    // (P1 deferred).
    private async void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }

    private void CommitOrPush_OnClick(object? sender, RoutedEventArgs e)
    {
        // 1.0.1: the previous shape was a
        // disabled-by-design stub ("Lands in
        // Wave 6"). Wave 6 actually shipped
        // the Git status modal — but the
        // button lives in this panel, not
        // the modal. Open the modal via the
        // same event-bus pattern
        // InsertReferenceRequested uses
        // (EnvironmentPanelViewModel.Raise*
        // → MainWindow subscribes → calls
        // OpenGitStatusCommand). The user
        // gets a single click from the
        // Environment panel into the full
        // commit UI. Push / PR work itself
        // is still future (no GitHub OAuth),
        // so the second button stays
        // disabled — see CreatePr_OnClick.
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            vm.RaiseOpenGitStatusRequested();
        }
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

    // Wave 5 (plan §5): the chevron button next to "变更" opens the
    // Diff view. Wave 6 owns the full file-list / line-level diff /
    // 1.0.1: was a no-op stub
    // ("Wave 6 will route the click
    // through the parent MainWindow").
    // Wave 6 actually shipped the full
    // Git status modal — including the
    // Diff viewer — so the click now
    // routes through the same
    // OpenGitStatusRequested event the
    // 提交或推送 button uses. MainWindow
    // subscribes and calls its
    // OpenGitStatusCommand; the modal
    // lands with the Diff tab already
    // active (modal-level state, not
    // panel-level).
    private void OpenDiff_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            vm.RaiseOpenGitStatusRequested();
        }
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

    // 1.0.1: per-row "×" click handler. Drops
    // the Source from the registry via the
    // panel VM. The button's Tag carries the
    // row's Id (just the id, not the full
    // Source) so the handler can target the
    // registry entry without taking a
    // dependency on the row's full Source
    // object. async void is intentional here
    // because the click handler can't be
    // awaited by Avalonia's input pipeline —
    // the try/catch belt-and-braces mirrors
    // the StopBackgroundProcess_OnClick
    // pattern, in case a future change lets
    // an exception escape the registry's
    // persistence path.
    private async void SourceRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sourceId }
            || string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }
        if (DataContext is EnvironmentPanelViewModel vm)
        {
            try
            {
                await vm.RemoveSourceAsync(sourceId);
            }
            catch
            {
                // Registry's persistence path is
                // expected to swallow its own
                // errors. This catch is a
                // belt-and-braces fallback in
                // case a future change lets an
                // exception escape (e.g. JSON
                // write failure). Crashing the
                // panel over a delete-click
                // would be worse than silently
                // leaving the row in place.
            }
        }
    }

    // 1.0.1: Sources section header
    // "清空" button. Walks every
    // persisted source id in the VM
    // and calls RemoveAsync on each.
    // The registry's Changed event
    // re-mirrors the empty state back
    // into the panel automatically.
    // Same try/catch shape as the
    // per-row × handler — async void
    // must not crash the panel.
    private async void ClearSources_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnvironmentPanelViewModel vm)
        {
            return;
        }
        try
        {
            await vm.ClearSourcesAsync();
        }
        catch
        {
            // Registry swallows its own
            // errors; this is the
            // belt-and-braces fallback.
        }
    }

    // 1.0.1: per-row header click toggles the
    // row's inline expand panel. The display
    // name is the natural click target — the
    // user reads the truncated 60-char
    // preview, sees it's not the full body,
    // and clicks to see the rest. PointerPressed
    // (rather than Click) because the XAML
    // wraps the header in a non-Button
    // StackPanel; Click is a Button-only event
    // and we want the entire header strip to
    // register the press. Mark Handled so the
    // bubbling press doesn't double-fire
    // through the parent ItemsControl.
    private void SourceRowHeader_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not StackPanel { DataContext: SourceRowViewModel row })
        {
            return;
        }
        row.IsExpanded = !row.IsExpanded;
        e.Handled = true;
    }
}
