using System.Windows.Input;

namespace AIChat.App.ViewModels;

// Small ICommand wrapper so buttons and menu items can call ViewModel methods.
// In WPF, command CanExecute controls whether the bound UI element is enabled.
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    // Call this when state changes, for example when DraftMessage becomes empty.
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
