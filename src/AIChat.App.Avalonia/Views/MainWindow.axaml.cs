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
    private readonly AvaloniaClipboardService _clipboard;
    private readonly IThemeService _theme;

    // Whether the user is currently parked at (or within ~32px of) the
    // bottom of the conversation view. Updated by the ScrollChanged
    // listener; the auto-scroll-on-add path consults it. Promoted to a
    // field so the "↓ N 条新消息" pill click can also flip it true —
    // after ScrollToEnd runs the ScrollChanged handler would update
    // it too, but doing it eagerly keeps the very next streaming
    // chunk from falling into the "user is scrolled up" branch while
    // the scroll is still settling.
    private bool _isUserAtBottom = true;

    public MainWindow(
        MainWindowViewModel viewModel,
        AvaloniaProjectPicker picker,
        AvaloniaClipboardService clipboard,
        IThemeService theme)
    {
        InitializeComponent();
        DataContext = viewModel;
        _picker = picker;
        _clipboard = clipboard;
        _theme = theme;
        // The picker and clipboard service both need the window as its
        // TopLevel so the dialog / clipboard can attach to the right
        // window. We set it here rather than in App.axaml.cs because the
        // constructor is the only place that has a stable reference to
        // this.
        _picker.TopLevel = this;
        _clipboard.TopLevel = this;

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
            // ⌘⇧M opens the memory editor. Add / delete the current
            // project's memory entries. ⌘/ still surfaces the read-
            // only /memory summary in the activity feed for the
            // quick-glance use case.
            Gesture = new KeyGesture(Key.M, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = viewModel.OpenMemoryEditorCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘G surfaces the current project's git status (branch +
            // uncommitted change list) as a system bubble. The user can
            // re-fire it to refresh — the workspace change service reads
            // git state on every call, so there's no staleness.
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta),
            Command = new RelayCommand(async () =>
            {
                var (handled, slashResult) = await SlashCommandHandler.TryExecuteAsync("/git", viewModel);
                if (handled && slashResult is not null)
                {
                    viewModel.ActivityFeed.Add(slashResult.Title, slashResult.Body, "系统");
                    viewModel.StatusMessage = slashResult.Title + "。";
                }
            })
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘⇧G opens the full git status / diff viewer modal. File
            // list on the left, diff on the right. ⌘G stays as the
            // quick bubble for the lightweight glance.
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = viewModel.OpenGitStatusCommand
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
            Command = new RelayCommand(async () =>
            {
                var (handled, slashResult) = await SlashCommandHandler.TryExecuteAsync("/help", viewModel);
                if (handled && slashResult is not null)
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
                else if (viewModel.IsMemoryEditorOpen)
                {
                    viewModel.IsMemoryEditorOpen = false;
                }
                else if (viewModel.IsGitStatusOpen)
                {
                    viewModel.IsGitStatusOpen = false;
                }
            })
        });

        // Auto-scroll the conversation view to the bottom whenever a new
        // activity item arrives — but only if the user is already at
        // (or near) the bottom. If they've scrolled up to re-read an
        // earlier bubble during a long run, the previous behaviour was
        // to yank them back down on every streaming chunk, which makes
        // it impossible to actually read history while the agent
        // works. Once they scroll back to the bottom, auto-scroll
        // resumes naturally.
        //
        // The "near bottom" threshold is in DIPs; 32px covers the
        // rounded scrollbar's end-stop rounding plus a couple of
        // subpixel slop rows so the user's wheel scroll doesn't have
        // to land exactly on Extent - Viewport to count as "following
        // along".
        //
        // When the user is scrolled UP, the new bubble goes into the
        // activity feed but does NOT scroll the view; instead, a
        // counter on the host VM ticks up and the floating
        // "↓ N 条新消息" pill (in the conversation panel) becomes
        // visible. Clicking the pill scrolls to the bottom and clears
        // the counter. Scrolling back to the bottom on the wheel also
        // clears the counter — the user has "seen" the new content
        // by virtue of being at the bottom.
        const double AtBottomThreshold = 32;

        ConversationScroll.ScrollChanged += (_, _) =>
        {
            var offsetY = ConversationScroll.Offset.Y;
            var extent = ConversationScroll.Extent.Height;
            var viewport = ConversationScroll.Viewport.Height;
            _isUserAtBottom = (offsetY + viewport) >= (extent - AtBottomThreshold);
            if (_isUserAtBottom && viewModel.UnseenMessageCount > 0)
            {
                viewModel.ClearUnseenMessageCount();
            }
        };

        viewModel.ActivityFeed.Activity.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // Clear() (e.g. /new, /clear, the sidebar "新对话"
                // button) fires Reset. Snap the scroll back to the
                // top of the freshly-empty feed and reset the
                // at-bottom flag so the next bubble added lands
                // visibly — without this, the ScrollViewer's
                // preserved offset would point past the end of the
                // new content, and the auto-scroll branch below
                // would stay disabled (at-bottom was false from
                // the previous conversation).
                _isUserAtBottom = true;
                Dispatcher.UIThread.Post(() =>
                    ConversationScroll.ScrollToHome(),
                    DispatcherPriority.Background);
                return;
            }
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                return;
            }
            if (_isUserAtBottom)
            {
                // Use a small post to let the ItemsControl realise the
                // new container before we ask for the scroll offset.
                Dispatcher.UIThread.Post(() =>
                    ConversationScroll.ScrollToEnd(),
                    DispatcherPriority.Background);
            }
            else
            {
                viewModel.IncrementUnseenMessageCount();
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

    // "↓ N 条新消息" pill click: jump to the bottom, clear the
    // unseen counter, and flip the "at bottom" flag eagerly so the
    // very next streaming bubble (which can arrive before
    // ScrollChanged has a chance to fire) keeps auto-following.
    private void NewMessagePill_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        viewModel.ClearUnseenMessageCount();
        _isUserAtBottom = true;
        ConversationScroll.ScrollToEnd();
    }

    private void ProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectId })
        {
            return;
        }
        SafeRun(() =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return Task.CompletedTask;
            return viewModel.SelectProjectFromUiAsync(projectId);
        });
    }

    private void ConversationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string conversationId })
        {
            return;
        }
        SafeRun(() =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return Task.CompletedTask;
            return viewModel.SelectConversationFromUiAsync(conversationId);
        });
    }

    private void AddProject_OnClick(object? sender, RoutedEventArgs e)
    {
        SafeRun(async () =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            var path = await _picker.PickProjectFolderAsync();
            if (path is { Length: > 0 })
            {
                await viewModel.AddProjectFromUiAsync(path);
            }
        });
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

    // Memory editor modal: same scrim / content pattern.
    private void MemoryEditorScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsMemoryEditorOpen = false;
        }
    }

    private void MemoryEditorContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // Git status / diff viewer modal.
    private void GitStatusScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsGitStatusOpen = false;
        }
    }

    private void GitStatusContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // Tool approval modal: window-modal so the user can't queue another
    // prompt while a write is pending. The scrim click rejects (the
    // user clicking outside the dialog is functionally a "no, don't
    // do that" gesture). The agent loop's PresentRequestAsync is
    // still awaiting on the TCS, so this resolves it with a Reject
    // and the run ends.
    private void ToolApprovalScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.Approval.RejectCommand.CanExecute(null))
        {
            viewModel.Approval.RejectCommand.Execute(null);
        }
    }

    private void ToolApprovalContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    // Wraps an async event-handler body so any uncaught exception
    // becomes a user-visible status message + log instead of an
    // unhandled-exception crash on the dispatcher. Avalonia's
    // dispatcher treats async void exceptions as fatal; the only
    // safe pattern for XAML event handlers is "async void with a
    // try/catch at the root".
    private async void SafeRun(Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.StatusMessage = $"操作失败：{ex.Message}";
            }
        }
    }

    // Prompt input: Cmd+Enter (or Ctrl+Enter on non-mac) sends. The default
    // Enter key still inserts a newline because the box is multi-line.
    // Cmd+V / Ctrl+V intercepts the paste when the clipboard has an
    // image: text pastes fall through to the default TextBox handler
    // (we don't touch them), image pastes are saved as pending
    // attachments and shown above the input.
    private void PromptInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        SafeRun(async () =>
        {
            // Image paste: intercept ⌘V / Ctrl+V when the clipboard has
            // an image. The default TextBox paste handler would do
            // nothing for a non-text payload, but consuming the key event
            // explicitly makes the contract obvious.
            if (e.Key == Key.V &&
                (e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                 e.KeyModifiers.HasFlag(KeyModifiers.Control)) &&
                DataContext is MainWindowViewModel promptVm &&
                _clipboard.IsAvailable)
            {
                var bitmap = await _clipboard.TryGetBitmapAsync();
                if (bitmap is not null)
                {
                    e.Handled = true;
                    promptVm.PendingAttachments.AddPastedImage(bitmap);
                    bitmap.Dispose();
                    return;
                }
            }

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
        });
    }

    // Command palette: arrow keys move selection, Enter executes, Escape
    // closes. The list box already has focus navigation; we just need to
    // intercept these to keep the search box in focus while cycling.
    private void CommandPalette_OnKeyDown(object? sender, KeyEventArgs e)
    {
        SafeRun(async () =>
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
        });
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
