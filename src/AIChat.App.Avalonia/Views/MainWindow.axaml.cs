using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly AvaloniaProjectPicker _picker;
    private readonly IThemeService _theme;

    public MainWindow(MainWindowViewModel viewModel, AvaloniaProjectPicker picker, IThemeService theme)
    {
        InitializeComponent();
        DataContext = viewModel;
        _picker = picker;
        _theme = theme;
        // The picker needs the window as its TopLevel so the dialog
        // attaches to the right window. We set it here rather than in
        // App.axaml.cs because the constructor is the only place that has
        // a stable reference to this.
        _picker.TopLevel = this;

        // Window-level keyboard shortcuts. The per-input shortcuts (Cmd+Enter
        // on the prompt box, arrows in the command palette) are handled in
        // their respective KeyDown handlers.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.K, KeyModifiers.Meta),
            Command = viewModel.OpenCommandPaletteCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // Avalonia's OemComma is the platform-neutral comma key.
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
            Command = viewModel.OpenSettingsCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta),
            Command = viewModel.NewConversationCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = viewModel.ToggleThemeCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘. is the platform-standard "cancel current operation"
            // shortcut. Forwards to StopTaskCommand which is gated by
            // CanStopTask (= IsRunning) so it does nothing when idle.
            Gesture = new KeyGesture(Key.OemPeriod, KeyModifiers.Meta),
            Command = viewModel.StopTaskCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘R re-sends the last user prompt. Gated by CanRetry on
            // RetryLastTaskCommand so it only fires when the previous
            // run failed or was stopped.
            Gesture = new KeyGesture(Key.R, KeyModifiers.Meta),
            Command = viewModel.RetryLastTaskCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘L focuses the prompt input. The browser convention; also
            // matches Slack / Discord / Linear. Routed through a small
            // RelayCommand so the code-behind can call Focus() on the
            // named TextBox and SelectAll() so the user can start
            // overwriting immediately.
            Gesture = new KeyGesture(Key.L, KeyModifiers.Meta),
            Command = new RelayCommand(() => FocusPromptInput())
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘⇧R toggles read-only / no-write mode. The agent still
            // reads files and searches, but every tool call that would
            // mutate the project is blocked (and any pending one is
            // auto-rejected without prompting the user). Useful for
            // exploring an unfamiliar codebase.
            Gesture = new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = new RelayCommand(() => viewModel.NoWriteMode = !viewModel.NoWriteMode)
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘⇧K is Slack / Discord's "clear channel" shortcut. In our
            // case it clears the current activity feed (ActivityFeed.Clear)
            // and is a no-op when the agent is running so the user
            // can't accidentally nuke an in-flight run.
            Gesture = new KeyGesture(Key.K, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = new RelayCommand(() =>
            {
                if (viewModel.IsRunning)
                {
                    viewModel.StatusMessage = "运行中，无法清空。";
                    return;
                }
                viewModel.ActivityFeed.Clear();
                viewModel.StatusMessage = "已清空。";
            })
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘/ is VS Code's "show help" convention. Drops a /help
            // result bubble in the activity feed so the user can see
            // the available commands without leaving the composer.
            Gesture = new KeyGesture(Key.OemQuestion, KeyModifiers.Meta),
            Command = new RelayCommand(() =>
            {
                SlashCommandHandler.TryExecute("/help", viewModel, out var slashResult);
                if (slashResult is not null)
                {
                    viewModel.ActivityFeed.Add(slashResult.Title, slashResult.Body, "系统");
                    viewModel.StatusMessage = slashResult.Title + "。";
                }
            })
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(() =>
            {
                if (viewModel.IsCommandPaletteOpen)
                {
                    viewModel.IsCommandPaletteOpen = false;
                }
                else if (viewModel.IsSettingsOpen)
                {
                    viewModel.IsSettingsOpen = false;
                }
            })
        });

        // Auto-scroll the conversation view to the bottom whenever a new
        // activity item arrives. The user is most often following along
        // live, and the alternative (manually scrolling after every
        // bubble lands) makes the conversation feel sluggish.
        // Clear is the only mutation that should NOT scroll — it resets
        // the view before the next conversation starts.
        viewModel.ActivityFeed.Activity.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Use a small post to let the ItemsControl realise the new
                // container before we ask for the scroll offset.
                Dispatcher.UIThread.Post(() =>
                    ConversationScroll.ScrollToEnd(),
                    DispatcherPriority.Background);
            }
        };

        // Focus the command-palette search input every time the palette opens
        // so the user can start typing immediately.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsCommandPaletteOpen) &&
                viewModel.IsCommandPaletteOpen)
            {
                CommandSearchInput.Focus();
            }
        };
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInsideButton(e.Source))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        BeginMoveDrag(e);
    }

    private void Minimize_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FocusPromptInput()
    {
        PromptInput.Focus();
        // SelectAll so ⌘L behaves like a browser address bar: land in
        // the field with everything pre-selected, ready to overwrite.
        PromptInput.SelectAll();
    }

    // Edit previous user message: copy the bubble's Detail back into
    // the prompt input and focus it. The user can tweak the text and
    // press Enter to send again. The old bubble stays in the feed (so
    // the conversation history is preserved); the new send adds a
    // fresh user bubble above the assistant's response.
    private void EditUserBubble_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.DraftPrompt = text;
        PromptInput.Focus();
        // Put the caret at the end rather than selecting all — the
        // user clicked to edit, not to replace wholesale.
        PromptInput.CaretIndex = text.Length;
    }

    private async void ProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectId } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SelectProjectFromUiAsync(projectId);
    }

    private async void ConversationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string conversationId } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SelectConversationFromUiAsync(conversationId);
    }

    private async void AddProject_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var path = await _picker.PickProjectFolderAsync();
        if (path is { Length: > 0 })
        {
            await viewModel.AddProjectFromUiAsync(path);
        }
    }

    // Empty-state welcome card click: fill the prompt with the suggested
    // task and focus the input so the user can press Enter immediately.
    private void EmptyStateCard_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string prompt } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.DraftPrompt = prompt;
        PromptInput.Focus();
    }

    // Click outside the command palette (on the scrim) closes it. The inner
    // Border's PointerPressed marks the event handled so the click doesn't
    // bubble up to the scrim's handler.
    private void CommandPaletteScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsCommandPaletteOpen = false;
        }
    }

    private void CommandPaletteContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // Same pattern for the settings modal.
    private void SettingsScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsSettingsOpen = false;
        }
    }

    private void SettingsContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // Prompt input: Cmd+Enter (or Ctrl+Enter on non-mac) sends. The default
    // Enter key still inserts a newline because the box is multi-line.
    private async void PromptInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var isSend = e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                     e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!isSend)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.SendTaskCommand.CanExecute(null))
        {
            await viewModel.SendTaskCommand.ExecuteAsync(null);
        }
    }

    // Command palette: arrow keys move selection, Enter executes, Escape
    // closes. The list box already has focus navigation; we just need to
    // intercept these to keep the search box in focus while cycling.
    private async void CommandPalette_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                viewModel.CommandPalette.MoveNextCommand.Execute(null);
                break;
            case Key.Up:
                e.Handled = true;
                viewModel.CommandPalette.MovePreviousCommand.Execute(null);
                break;
            case Key.Enter:
                e.Handled = true;
                await viewModel.CommandPalette.ExecuteSelectedAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                viewModel.IsCommandPaletteOpen = false;
                break;
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private static bool IsInsideButton(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        for (var current = control; current is not null; current = current.Parent as Control)
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }
}
