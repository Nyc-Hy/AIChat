using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AIChat.Abstractions.Configuration;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Sources;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.Views;

internal partial class MainWindow : Window
{
    private readonly AvaloniaProjectPicker _picker;
    private readonly AvaloniaClipboardService _clipboard;
    private readonly IThemeService _theme;
    private readonly IToastService _toast;
    private readonly ISettingsHolder _settingsHolder;
    private readonly ISourceRegistry _sourceRegistry;
    private readonly IWebPageFetcher _webPageFetcher;
    // Set to true once ApplyPersistedBounds has run so the closing
    // handler knows the live Position / Size are the user-adjusted
    // values rather than the values we just restored from disk.
    private bool _boundsApplied;

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
        IThemeService theme,
        IToastService toast,
        ISettingsHolder settingsHolder,
        ISourceRegistry sourceRegistry,
        IWebPageFetcher webPageFetcher)
    {
        InitializeComponent();
        DataContext = viewModel;
        _picker = picker;
        _clipboard = clipboard;
        _theme = theme;
        _toast = toast;
        _settingsHolder = settingsHolder;
        _sourceRegistry = sourceRegistry;
        _webPageFetcher = webPageFetcher;
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
            Command = viewModel.AgentHost.StopTaskCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘R re-sends the last user prompt. Gated by CanRetry on
            // RetryLastTaskCommand so it only fires when the previous
            // run failed or was stopped.
            Gesture = new KeyGesture(Key.R, KeyModifiers.Meta),
            Command = viewModel.AgentHost.RetryLastTaskCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘O opens a folder picker so the user can add a new
            // project. Surfaces the action the palette was already
            // advertising (the palette item's shortcut column said
            // '⌘ O' but no keybinding existed).
            Gesture = new KeyGesture(Key.O, KeyModifiers.Meta),
            Command = new RelayCommand(() => AddProject_OnClick(this, new RoutedEventArgs()))
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘T runs a connection test against the current model.
            // Same shape as the palette's "测试当前模型" entry
            // (which was also claiming the shortcut without backing).
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta),
            Command = new AsyncRelayCommand(() => RunSafelyAsync(
                () => viewModel.Provider.TestProviderCommand.ExecuteAsync(null)))
        });
        KeyBindings.Add(new KeyBinding
        {
            // ⌘⇧C copies the last AI reply. Mirrors /copy — same
            // handler, same status feedback (system bubble +
            // status-bar message). The palette was advertising this
            // shortcut without a binding.
            Gesture = new KeyGesture(Key.C, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = new AsyncRelayCommand(() => RunSafelyAsync(
                () => KeyCommandBridge.RunSlashCommandAsync(viewModel, "/copy")))
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
            // ⌘⇧V toggles auto-verify. The page-header pill surfaces the
            // current state; this is the keyboard shortcut to flip it.
            // Matches the (⌘⇧V 切换) hint in the pill tooltip and the
            // palette item's shortcut column — both were already
            // claiming the shortcut but the binding was missing.
            Gesture = new KeyGesture(Key.V, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = new RelayCommand(() => viewModel.Settings.AutoVerify = !viewModel.Settings.AutoVerify)
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
            Command = new AsyncRelayCommand(() => RunSafelyAsync(
                () => KeyCommandBridge.RunSlashCommandAsync(viewModel, "/git", "系统")))
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
                if (viewModel.AgentHost.IsRunning)
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
            // The natural "show me help" gesture. Opens the
            // shortcuts cheat-sheet modal (the more discoverable,
            // visually richer alternative to the /help slash
            // command — the slash command still works for users
            // who prefer typing /help in the prompt).
            Gesture = new KeyGesture(Key.OemQuestion, KeyModifiers.Meta),
            Command = new RelayCommand(() => viewModel.OpenShortcutsCommand.Execute(null))
        });
        KeyBindings.Add(new KeyBinding
        {
            // F5 is the conventional "refresh" key and the palette's
            // "刷新状态" entry advertises it in the shortcut column.
            // Without this binding the palette was promising a shortcut
            // that did nothing on keypress.
            Gesture = new KeyGesture(Key.F5),
            Command = viewModel.RefreshCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            // Sprint 0.5: toggle the right-side Environment panel.
            // ⌘⇧E follows the "Cmd+Shift+letter for app-level toggles"
            // pattern (⌘⇧T theme, ⌘⇧V auto-verify, ⌘⇧R read-only,
            // ⌘⇧M memory editor). The E key is free.
            Gesture = new KeyGesture(Key.E, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = viewModel.ToggleEnvironmentPanelCommand
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(() =>
            {
                if (viewModel.Approval.HasPendingApproval &&
                    viewModel.Approval.RejectCommand.CanExecute(null))
                {
                    viewModel.Approval.RejectCommand.Execute(null);
                    return;
                }
                // Modal priority order — topmost wins. Each
                // entry is a (isOpen, close) pair; first
                // open one closes. Mirrors
                // MainWindowViewModel.CloseAllModals for
                // the "all modals down" path; both lists
                // are kept in sync.
                // Wave 11 refactor: previously this was 9
                // explicit `else if` blocks; new modals
                // added one branch each (Wave 8-10 grew
                // the chain from 6 to 9). The priority
                // list makes the next modal a 1-line
                // append.
                var priority = new (bool IsOpen, Action Close)[]
                {
                    (viewModel.IsCommandPaletteOpen, () => viewModel.IsCommandPaletteOpen = false),
                    (viewModel.IsSettingsOpen, () => viewModel.IsSettingsOpen = false),
                    (viewModel.IsMemoryEditorOpen, () => viewModel.IsMemoryEditorOpen = false),
                    (viewModel.IsGitStatusOpen, () => viewModel.IsGitStatusOpen = false),
                    (viewModel.IsRunHistoryOpen, () => viewModel.IsRunHistoryOpen = false),
                    (viewModel.IsShortcutsOpen, () => viewModel.IsShortcutsOpen = false),
                    (viewModel.IsPluginsOpen, () => viewModel.IsPluginsOpen = false),
                    (viewModel.IsScheduledOpen, () => viewModel.IsScheduledOpen = false),
                    (viewModel.IsSitesOpen, () => viewModel.IsSitesOpen = false),
                };
                foreach (var modal in priority)
                {
                    if (modal.IsOpen)
                    {
                        modal.Close();
                        return;
                    }
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
            if (_isUserAtBottom && viewModel.MessageScroll.UnseenMessageCount > 0)
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
                // top of the freshly-empty feed, reset the
                // at-bottom flag, and zero the unseen counter so
                // the "↓ N 条新消息" pill doesn't reappear the
                // moment the panel becomes visible again on the
                // next send.
                _isUserAtBottom = true;
                viewModel.ClearUnseenMessageCount();
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
                CommandPalette.FocusSearchInput();
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

    // Sprint 0.5+: sidebar top navigation layer. All three buttons are
    // present-but-disabled by design — they match the Codex visual surface
    // so the user sees the parity shape, but the actions land in their
    // respective waves:
    //   - ModeSwitcher  → Wave 2 (multi-mode support)
    //   - Search        → Wave 3 (history / settings search)
    //   - Notifications → Wave 7 (subagent / background process events)
    // For now they show a toast on click so the user knows the icon is
    // alive and labelled with the wave that delivers it.
    private void ModeSwitcher_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast.Show("模式切换 — 默认模式（Wave 2 接入）", ToastLevel.Info);
    }

    private void Search_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast.Show("搜索 — Wave 3 接入", ToastLevel.Info);
    }

    private void Notifications_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast.Show("通知 — Wave 7 接入", ToastLevel.Info);
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
        viewModel.AgentHost.DraftPrompt = text;
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
        viewModel.MessageScroll.ClearUnseenMessageCount();
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

    // Codex parity: trailing `...` button on the selected project row.
    // Re-opens the row's context flyout so the existing 删除项目 entry
    // (and any future ones — "重命名" / "在 Finder 中显示" / etc.) shows
    // up under the cursor without the user having to right-click.
    // Implemented via a manual open because the inner Button consumes
    // the click event and prevents the outer ContextFlyout from auto-
    // opening on this click. The outer Button's Click handler still
    // fires first (we don't stop the event), so the project also gets
    // selected — same as Codex.
    private void ProjectMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectId } button)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        SafeRun(() => viewModel.SelectProjectFromUiAsync(projectId));

        // Find the parent project Button and open its context flyout.
        // Walk up the visual tree until we find a Button whose Tag is
        // this project's id; that's the row that owns the MenuFlyout.
        // We use StyledElement (the base that exposes Parent) and cast
        // each level to Button for the Tag-based lookup.
        StyledElement? parent = button.Parent;
        while (parent is not null)
        {
            if (parent is Button row
                && row.Tag is string rowId
                && string.Equals(rowId, projectId, StringComparison.OrdinalIgnoreCase)
                && row.ContextFlyout is { } flyout)
            {
                flyout.ShowAt(row);
                break;
            }
            parent = parent.Parent;
        }
    }

    // Codex parity: trailing edit icon. Wave 6 will wire this to the
    // inline rename popover; for now it just opens the same context
    // flyout as the `...` button so the user gets a feedback signal
    // that the click landed (Codex also has this fallback).
    private void ProjectEdit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectId })
        {
            return;
        }
        // Same UX as ProjectMenu_OnClick for now; future slices
        // (Wave 6) replace the body with a popover showing the
        // project path + rename / delete controls.
        SafeRun(() =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return Task.CompletedTask;
            return viewModel.SelectProjectFromUiAsync(projectId);
        });
        _toast?.Show("编辑项目 — Wave 6 接入", ToastLevel.Info);
    }

    // Codex parity: composer "+" (file picker). The ⌘V paste-into-prompt
    // path already works; this button opens a system file picker (Wave
    // 6+ lands the implementation, this click is the visible surface).
    private void AddAttachment_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast?.Show("附件选择器 — Wave 7 接入（用 ⌘V 粘贴图片）", ToastLevel.Info);
    }

    // Codex parity: composer mic (push-to-talk). The actual STT wiring
    // needs a model that supports audio input — Wave 4.5 picks the
    // provider and records. For now the click just shows a toast so
    // the user knows the click landed.
    private void Mic_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast?.Show("语音输入 — Wave 4.5 接入", ToastLevel.Info);
    }

    // Codex parity: status bar "+" button. Opens a small popup with
    // "新建项目 / 新建对话 / 新建计划" shortcuts. The popup itself
    // lands in Wave 6; for now we trigger the existing new-project
    // / new-conversation commands sequentially and let the user
    // click again if they meant a different one. (Codex actually
    // shows a single dropdown, but a click-through to a picker is
    // a fine starting point.)
    private void StatusBarAdd_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast?.Show("新建入口 — 用 ⌘O 加项目 / ⌘N 新对话", ToastLevel.Info);
    }

    // Codex parity: clicking a "最近" item is a no-op for now — the
    // real routing (open the conversation / re-run the search) lands
    // in Wave 6. We toast the title so the user sees the click landed
    // and knows which item they triggered.
    private void RecentItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string title })
        {
            _toast?.Show($"打开最近：{title}（Wave 6 接入）", ToastLevel.Info);
        }
    }

    // Wave 3 (plan §3.1): sidebar "Standalone" section "+" button.
    // Creates a new Standalone ChatSession and pushes it into the
    // sidebar list. The main view-model owns the Standalone list
    // (it spans the whole app, not per-project).
    private void NewStandaloneInline_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        SafeRun(() =>
        {
            viewModel.NewStandaloneConversationCommand.Execute(null);
            return Task.CompletedTask;
        });
    }

    // Wave 3: clicking a Standalone conversation card routes to the
    // activity feed (just like project conversations). The persisted
    // session id is on the Tag; the full ChatSession roundtrip is
    // done by the view-model (loads from disk, hands to ActivityFeed).
    private void StandaloneConversation_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sessionId })
        {
            return;
        }
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        SafeRun(() => viewModel.OpenStandaloneConversationAsync(sessionId));
    }

    // Inline rename (Wave 3 + 7.4). Same shape as the project-side
    // conversation rename: double-click / Enter commits, Esc rolls
    // back. Wave 3 ships the visible affordance + persistence; the
    // keybindings settle in a later polish pass.
    private void StandaloneRename_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ConversationCardViewModel card })
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            card.CommitRenameCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            card.CancelRenameCommand.Execute(null);
        }
    }

    private void StandaloneRename_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ConversationCardViewModel card })
        {
            card.CommitRenameCommand.Execute(null);
        }
    }

    // Wave 3 (plan §3.2): set primary folder on a multi-folder
    // workspace. The Tag carries the folder id; the view-model
    // (ProjectSidebarViewModel) finds the matching workspace, sets
    // its PrimaryFolderId, and persists.
    private void SetPrimaryFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folderId })
        {
            return;
        }
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        SafeRun(() => viewModel.Sidebar.SetPrimaryFolderAsync(folderId));
    }

    // Wave 4 (plan §4): composer "+" menu items. Each opens the
    // appropriate picker / paste / placeholder. File picker routes
    // through the existing AvaloniaProjectPicker; image paste uses
    // the system clipboard; @file and source items land in Wave 6/7
    // (the menu shows them as disabled placeholders so the user can
    // see the full surface now).

    private void AddFileAttachment_OnClick(object? sender, RoutedEventArgs e)
    {
        // Compose the same flow the Add Project button uses: project
        // picker → if a single file is chosen, we attach it to the
        // current run. For now the picker is folder-only, so this
        // routes through AddProjectAsync and reuses the same OS
        // dialog. A dedicated file picker is a Wave 6 follow-up; for
        // Wave 4 the menu's existence + the toast feedback is the
        // visible surface we ship.
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        // Use the existing Add Project picker as a stand-in; the user
        // can pick any folder root and we attach the path as a
        // file-context. The full file picker (multi-file) lands in
        // Wave 6 alongside the @file reference picker.
        _ = _picker.PickProjectFolderAsync().ContinueWith(task =>
        {
            if (task.Result is PickerResult.Picked picked && !string.IsNullOrEmpty(picked.Path))
            {
                // For a real file picker the path would be a file URI;
                // for the folder picker stand-in we paste the path into
                // the prompt as a @-reference so the agent knows the
                // file/folder the user is interested in.
                viewModel.AgentHost.DraftPrompt += $" @{picked.Path} ";
                _toast?.Show($"已附加：{picked.Path}", ToastLevel.Info);
            }
        });
    }

    // "+" → "图片 / 文件" submenu. Opens a StorageProvider file
    // picker that accepts arbitrary file types (not just images,
    // despite the menu label) and forwards the selected paths
    // through the same PendingAttachments.AddFile path that
    // drag-and-drop uses. The 1.0 shape was a placeholder toast
    // pointing the user at ⌘V — 1.0.1 wires the real picker so
    // the composer "+" surface actually does what it advertises.
    //
    // Cancellation (user closes the dialog with no selection) is
    // silent — same convention as ExportConversationMenuItem and
    // the project picker.
    private async void AddImageAttachment_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "添加附件",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
                    new FilePickerFileType("图片") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.svg" } },
                    new FilePickerFileType("文档") { Patterns = new[] { "*.pdf", "*.doc", "*.docx", "*.txt", "*.md", "*.rtf" } },
                    new FilePickerFileType("代码 / 数据") { Patterns = new[] { "*.json", "*.xml", "*.yaml", "*.yml", "*.csv", "*.tsv" } },
                },
            });
            if (files is null || files.Count == 0)
            {
                return;
            }
            var added = 0;
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                try
                {
                    viewModel.AgentHost.PendingAttachments.AddFile(path);
                    added++;
                }
                catch (Exception ex)
                {
                    viewModel.StatusMessage = $"无法添加 {Path.GetFileName(path)}：{ex.Message}";
                }
            }
            if (added > 0)
            {
                viewModel.StatusMessage = $"已添加 {added} 个附件。";
                PromptInput.Focus();
            }
        }
        catch (Exception ex)
        {
            // The CrashReporter hook will already have caught
            // any unexpected exception; we just want a user-
            // visible message instead of a silent failure.
            viewModel.StatusMessage = $"添加附件失败：{ex.Message}";
        }
    }

    private void AddAtFile_OnClick(object? sender, RoutedEventArgs e)
    {
        // @file picker (Wave 6 follow-up — for Wave 4 we just paste a
        // placeholder so the user can see the menu surface respond).
        _toast?.Show("@file 引用 — Wave 6 接入完整 picker", ToastLevel.Info);
    }

    private async void AddClipboardSource_OnClick(object? sender, RoutedEventArgs e)
    {
        // Wave 7 (parity plan §7 Wave 7) first slice: the
        // "+" / "剪贴板快照" menu item now reads the
        // platform clipboard, persists the text as a
        // Source, and surfaces it in the Environment
        // panel's Sources section. Empty / non-text
        // clipboards surface a user-visible error so the
        // user knows the click took effect (and why
        // nothing was saved).
        if (!_clipboard.IsAvailable)
        {
            _toast?.Show("无法访问剪贴板（无 TopLevel 或测试环境）", ToastLevel.Warning);
            return;
        }
        var text = await _clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            _toast?.Show("剪贴板为空或不是文本。", ToastLevel.Warning);
            return;
        }
        var firstLine = text.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        var display = firstLine.Length > 60 ? firstLine[..60] + "…" : firstLine;
        var source = new AIChat.Domain.Sources.Source
        {
            Kind = "clipboard",
            DisplayName = display,
            Content = text,
            CapturedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["length"] = text.Length.ToString(),
            },
        };
        await _sourceRegistry.AddAsync(source);
        _toast?.Show($"已捕获 {text.Length} 字符到数据源。", ToastLevel.Success);
    }

    private async void AddWebSearchSource_OnClick(object? sender, RoutedEventArgs e)
    {
        // Wave 7 (parity plan §7 Wave 7) first slice:
        // the "+" / "网页搜索" menu item now opens a
        // URL input dialog, fetches the page, and
        // persists it as a Source (kind=web). The
        // existing Wave 7 placeholder toast is gone
        // — the click is real.
        var dialog = new UrlInputDialog(this);
        var url = await dialog.ShowAsync();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        var result = await _webPageFetcher.FetchAsync(url);
        if (result is null)
        {
            _toast?.Show($"无法抓取 {url}（网络失败 / 非 HTML / 内容过大）", ToastLevel.Warning);
            return;
        }
        // Use the page's <title> as the display name;
        // fall back to the host portion of the URL
        // when the page didn't declare a title. The
        // metadata keeps the original URL so the
        // agent can reference the source by URL
        // (rather than the full text) when it wants
        // to cite it.
        var display = string.IsNullOrWhiteSpace(result.Title)
            ? new Uri(result.Url).Host
            : result.Title;
        var source = new AIChat.Domain.Sources.Source
        {
            Kind = "web",
            DisplayName = display,
            Content = result.Content,
            CapturedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["url"] = result.Url,
                ["statusCode"] = result.StatusCode.ToString(),
            },
        };
        await _sourceRegistry.AddAsync(source);
        _toast?.Show($"已抓取 {result.Content.Length} 字符到数据源。", ToastLevel.Success);
    }

    private void AddPluginAttachment_OnClick(object? sender, RoutedEventArgs e)
    {
        _toast?.Show("插件 — Wave 8 接入", ToastLevel.Info);
    }

    // Wave 4 (plan §4): "追加要求" button. The button is currently
    // disabled — the actual queue / merge into the running agent's
    // step loop is a follow-up slice (the real design has to coordinate
    // with the agent's cancellation token + tool execution state).
    // For now the click surface is wired so the user can see the
    // parity shape land; the click handler is a no-op so the
    // disabled state stays correct.
    private void AppendFollowup_OnClick(object? sender, RoutedEventArgs e)
    {
        // No-op for now — the button is disabled. We don't even
        // show a toast because the user shouldn't be able to click.
        // This handler is here so the XAML click binding has a target;
        // the Wave 4 follow-up will replace it with the real queue
        // wiring (see plan §4 "发送、停止、追加要求、重试").
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

    // 2026-08-03: right-click "Export as Markdown" handler. The
    // view-model owns the export + write; the code-behind owns
    // the SaveFilePicker (Avalonia StorageProvider, requires a
    // live TopLevel) so the picker can be swapped for a headless
    // fake in tests without going through the file system.
    private async void ExportConversationMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string conversationId } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出对话为 Markdown",
                DefaultExtension = "md",
                ShowOverwritePrompt = true,
                SuggestedFileName = SanitizeFileName($"{conversationId}.md"),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } },
                },
            });
            if (file is null)
            {
                return; // User cancelled.
            }

            var bytes = await viewModel.ConversationList.ExportConversationToPathAsync(
                conversationId, file.Path.LocalPath);
            if (bytes is null)
            {
                _toast.Show("导出失败，请检查目标路径是否可写。", ToastLevel.Error);
            }
            else
            {
                _toast.Show($"已导出 {bytes} 字节到 {file.Name}。", ToastLevel.Success);
            }
        }
        catch (Exception ex)
        {
            // The CrashReporter hook will already have caught
            // any unexpected exception; we just want a user-
            // visible message instead of a silent failure.
            _toast.Show($"导出失败：{ex.Message}", ToastLevel.Error);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            buffer.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }
        return buffer.ToString();
    }

    // Inline-rename keyboard handler. Enter commits, Esc cancels.
    // LostFocus also commits, so Tab / click-out behaves the same
    // as Enter (matches how every chat app handles inline edit).
    private void ConversationRename_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not ConversationCardViewModel card)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SafeRun(() => card.CommitRenameCommand.ExecuteAsync(null));
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            card.CancelRenameCommand.Execute(null);
        }
    }

    private void ConversationRename_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox &&
            textBox.DataContext is ConversationCardViewModel card &&
            card.IsRenaming)
        {
            SafeRun(() => card.CommitRenameCommand.ExecuteAsync(null));
        }
    }

    private void AddProject_OnClick(object? sender, RoutedEventArgs e)
    {
        SafeRun(async () =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.Approval.HasPendingApproval) return;
            var result = await _picker.PickProjectFolderAsync();
            switch (result)
            {
                case PickerResult.Picked picked:
                    await viewModel.AddProjectFromUiAsync(picked.Path);
                    break;
                case PickerResult.Failed failed:
                    viewModel.StatusMessage = failed.Reason;
                    break;
                // Cancelled — stay silent; the user explicitly dismissed
                // the dialog and doesn't need a status message.
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

        viewModel.AgentHost.DraftPrompt = prompt;
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
    // Memory editor modal: extracted to Views/Controls/MemoryEditorView.

    // Git status / diff viewer modal: extracted to Views/Controls/GitStatusView.

    // Tool approval modal: extracted to Views/Controls/ToolApprovalView.
    // The scrim click rejects (the user clicking outside the dialog is
    // functionally a "no, don't do that" gesture). The agent loop's
    // PresentRequestAsync is still awaiting on the TCS, so this
    // resolves it with a Reject and the run ends.

    // Wraps an async event-handler body so any uncaught exception
    // becomes a user-visible status message + log instead of an
    // unhandled-exception crash on the dispatcher. Avalonia's
    // dispatcher treats async void exceptions as fatal; the only
    // safe pattern for XAML event handlers is "async void with a
    // try/catch at the root".
    private async void SafeRun(Func<Task> body)
    {
        await RunSafelyAsync(body);
    }

    private async Task RunSafelyAsync(Func<Task> body)
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
                    promptVm.AgentHost.PendingAttachments.AddPastedImage(bitmap);
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
            if (viewModel.AgentHost.SendTaskCommand.CanExecute(null))
            {
                await viewModel.AgentHost.SendTaskCommand.ExecuteAsync(null);
            }
        });
    }

    // Drag-and-drop file attachments into the composer.
    //
    // The handlers live on the root Grid (set via
    // DragDrop.AllowDrop="True" + DragDrop.Drop / DragOver / DragLeave
    // in MainWindow.axaml). The whole window is the drop target so the
    // user doesn't have to aim at the composer — they can drop anywhere
    // on the main content and the file lands in the pending-attachments
    // strip above the prompt.
    //
    // Avalonia 12's drag-drop API is the new IDataTransfer-based shape
    // (e.DataTransfer + DataTransferExtensions.TryGetFiles). The old
    // e.Data + DataFormats.Files path is marked obsolete; the new API
    // also handles OS file promises (the "real" file URI on macOS
    // instead of the "filename only" fallback) correctly.
    //
    // Directory drops are filtered out — the artifact pipeline is
    // per-file and a 1.0.1 follow-up will add a "include children"
    // path.
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer is not null &&
            DataTransferExtensions.TryGetFiles(e.DataTransfer) is { Length: > 0 };
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        IsDragOver = hasFiles;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        IsDragOver = false;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        IsDragOver = false;
        if (e.DataTransfer is null)
        {
            return;
        }
        var items = DataTransferExtensions.TryGetFiles(e.DataTransfer);
        if (items is null || items.Length == 0)
        {
            return;
        }
        e.Handled = true;
        SafeRun(async () => await AcceptDroppedFilesAsync(items));
    }

    // Resolves each IStorageItem to a local path, then forwards to
    // PendingAttachments.AddFile. Directories are skipped with a
    // user-visible status message (the per-file pipeline can't
    // recurse in the 1.0.1 first slice; a follow-up will add a
    // "include children" mode). Failures from AddFile (file gone,
    // perms, etc.) bubble up through the per-item try / catch so
    // a single bad file doesn't kill the whole drop.
    private async Task AcceptDroppedFilesAsync(IEnumerable<IStorageItem> items)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        var accepted = 0;
        var skipped = 0;
        foreach (var item in items)
        {
            if (item is IStorageFolder)
            {
                skipped++;
                continue;
            }
            var path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                skipped++;
                continue;
            }
            try
            {
                viewModel.AgentHost.PendingAttachments.AddFile(path);
                accepted++;
            }
            catch (Exception ex)
            {
                skipped++;
                viewModel.StatusMessage = $"无法添加 {Path.GetFileName(path)}：{ex.Message}";
            }
        }
        if (accepted > 0)
        {
            viewModel.StatusMessage = skipped > 0
                ? $"已添加 {accepted} 个附件（{skipped} 个跳过）。"
                : $"已添加 {accepted} 个附件。";
            PromptInput.Focus();
        }
        else if (skipped > 0)
        {
            viewModel.StatusMessage = "未添加任何附件（仅支持文件，不支持目录）。";
        }
        await Task.CompletedTask;
    }

    // Toggles the "drop here" overlay in the XAML. The overlay sits
    // above the conversation area and only fades in while a file
    // drag is hovering the window — the XAML binds IsVisible to
    // this property. Setter fires the OnPropertyChanged event
    // through the field, which is what the {Binding IsDragOver}
    // markup extension subscribes to.
    private bool _isDragOver;
    public static readonly DirectProperty<MainWindow, bool> IsDragOverProperty =
        AvaloniaProperty.RegisterDirect<MainWindow, bool>(
            nameof(IsDragOver), o => o.IsDragOver, (o, v) => o.IsDragOver = v);
    public bool IsDragOver
    {
        get => _isDragOver;
        set
        {
            if (_isDragOver == value)
            {
                return;
            }
            _isDragOver = value;
            // DirectProperty<T> needs the registered callback,
            // not the standard PropertyChanged event, to push the
            // value into Avalonia's binding system. RaiseAndSetIfChanged
            // handles both sides in one call.
            RaisePropertyChanged(IsDragOverProperty, oldValue: !value, newValue: value);
        }
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
                    if (await viewModel.CommandPalette.ExecuteSelectedAsync())
                    {
                        viewModel.IsCommandPaletteOpen = false;
                    }
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

    // Sprint 0.5: plan §7 Wave 2 acceptance — the Composer (prompt input)
    // should auto-receive focus on window open so the user can start
    // typing without first ⌘L. We focus on first show, NOT on every
    // activate (that would steal focus from the command palette while
    // the user is searching).
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // 2026-08-03: restore the persisted window position / size /
        // maximised state on the next UI tick, by which point the
        // settings holder is expected to have the loaded AppSettings
        // (the main view-model's RefreshCoreAsync runs the JSON load
        // before OnOpened fires). The bounds guard against a multi-
        // monitor user who disconnected the secondary screen between
        // sessions — if the saved origin is now off-screen we fall
        // back to a centred position so the window is reachable.
        Dispatcher.UIThread.Post(ApplyPersistedBounds, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => FocusPromptInput(), DispatcherPriority.Background);
    }

    private void ApplyPersistedBounds()
    {
        var settings = _settingsHolder.Current;
        // A non-zero width / height means the user has positioned
        // the window at least once. Zero (the schema default) is
        // treated as "not yet positioned" so the host applies the
        // platform default (Avalonia's WindowStartupLocation).
        var hasPosition = settings.WindowX != 0 || settings.WindowY != 0;
        var hasSize = settings.WindowWidth > 200 && settings.WindowHeight > 200;

        if (hasSize)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (hasPosition && IsPositionOnAnyScreen(settings.WindowX, settings.WindowY, Width, Height))
        {
            Position = new PixelPoint((int)settings.WindowX, (int)settings.WindowY);
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _boundsApplied = true;
    }

    // Returns true if the rectangle at (x, y, w, h) overlaps at
    // least one of the connected screen work areas. Used to keep a
    // user whose secondary monitor was unplugged from launching
    // AIChat on a screen they cannot see. The 64-pixel slop
    // tolerates minor DPI / taskbar changes.
    private bool IsPositionOnAnyScreen(double x, double y, double w, double h)
    {
        foreach (var screen in Screens.All)
        {
            var wa = screen.WorkingArea;
            var left = wa.X;
            var top = wa.Y;
            var right = wa.X + wa.Width;
            var bottom = wa.Y + wa.Height;
            if (x + w > left + 64 && x < right - 64 &&
                y + h > top + 64 && y < bottom - 64)
            {
                return true;
            }
        }
        return false;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Persist the live bounds so the next launch restores the
        // user's preferred layout. We skip the write if the
        // restore step never ran (e.g. the window was created and
        // immediately torn down during a smoke test) so we do not
        // overwrite a real position with the platform default
        // origin.
        if (!_boundsApplied)
        {
            return;
        }

        var settings = _settingsHolder.Current;
        var maximised = WindowState == WindowState.Maximized;
        settings.WindowMaximized = maximised;
        if (maximised)
        {
            // When the window is maximised the Position / Width /
            // Height are not user-meaningful (they are the
            // maximised state). Keep whatever was last saved so
            // un-maximising on the next launch restores the prior
            // floating bounds rather than the OS default.
            return;
        }

        settings.WindowX = Position.X;
        settings.WindowY = Position.Y;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
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
