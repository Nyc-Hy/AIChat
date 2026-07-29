using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;

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
    }

    private void ToggleTheme_OnClick(object? sender, RoutedEventArgs e)
    {
        _theme.CycleToNext();
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
