using AIChat.Domain.Projects;

namespace AIChat.App.ViewModels;

public sealed class ProjectVerificationCommandViewModel : ObservableObject
{
    public ProjectVerificationCommandViewModel(ProjectVerificationCommand command)
    {
        Command = command;
    }

    public ProjectVerificationCommand Command { get; }

    public string Id => Command.Id;

    public string Name
    {
        get => Command.Name;
        set
        {
            if (Command.Name == value)
            {
                return;
            }

            Command.Name = value;
            OnPropertyChanged();
        }
    }

    public string CommandText
    {
        get => Command.Command;
        set
        {
            if (Command.Command == value)
            {
                return;
            }

            Command.Command = value;
            OnPropertyChanged();
        }
    }

    public string Target
    {
        get => Command.WorkingDirectory;
        set
        {
            if (Command.WorkingDirectory == value)
            {
                return;
            }

            Command.WorkingDirectory = value;
            OnPropertyChanged();
        }
    }

    public int TimeoutSeconds
    {
        get => Command.TimeoutSeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 600);
            if (Command.TimeoutSeconds == normalized)
            {
                return;
            }

            Command.TimeoutSeconds = normalized;
            OnPropertyChanged();
        }
    }
}
