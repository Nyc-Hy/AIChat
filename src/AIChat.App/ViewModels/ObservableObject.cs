using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIChat.App.ViewModels;

// Base class for MVVM property change notifications. WPF bindings listen to
// PropertyChanged and repaint only the affected values.
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        // CallerMemberName means most setters do not have to pass their property
        // name manually.
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
