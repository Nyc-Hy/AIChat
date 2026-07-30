using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
