using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// One entry in the command palette. Designed for search-first UX:
// Title is the primary match, Subtitle is the secondary, Shortcut is the
// hint shown on the right. Action is a closure that runs the command;
// it returns true if the palette should close after execution.
public sealed class CommandItem
{
    public CommandItem(string title, string subtitle, string shortcut, string glyph, Func<Task<bool>> action)
    {
        Title = title;
        Subtitle = subtitle;
        Shortcut = shortcut;
        Glyph = glyph;
        Action = action;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Shortcut { get; }
    public string Glyph { get; }
    public Func<Task<bool>> Action { get; }
}

// Command palette view-model: search-as-you-type, keyboard navigable list
// of CommandItem. Owned by the MainWindowViewModel which feeds it the
// palette surface (an ItemsControl overlay); the host owns the
// IsCommandPaletteOpen bool that drives XAML visibility, and resets
// SearchText / SelectedIndex on open so the palette lands in a clean
// state every time.
public sealed partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<CommandItem> _allCommands = [];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private int selectedIndex;

    public ObservableCollection<CommandItem> FilteredCommands { get; } = [];

    // SearchText is the single source of truth; we rebuild the filtered
    // list on every change. CommandItem count is bounded (a dozen at most)
    // so a simple linear filter is plenty.
    partial void OnSearchTextChanged(string value)
    {
        RebuildFiltered();
    }

    public void RegisterCommands(IEnumerable<CommandItem> commands)
    {
        _allCommands.Clear();
        _allCommands.AddRange(commands);
        RebuildFiltered();
    }

    [RelayCommand]
    public void MoveNext()
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }
        SelectedIndex = (SelectedIndex + 1) % FilteredCommands.Count;
    }

    [RelayCommand]
    public void MovePrevious()
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }
        SelectedIndex = (SelectedIndex - 1 + FilteredCommands.Count) % FilteredCommands.Count;
    }

    public async Task<bool> ExecuteSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= FilteredCommands.Count)
        {
            return false;
        }
        var command = FilteredCommands[SelectedIndex];
        // Command's Action returns true if the palette should close
        // after running. The host flips its own IsCommandPaletteOpen
        // in response (via the command's return value), so this VM
        // doesn't need its own IsOpen mirror anymore — the previous
        // IsOpen + OnIsOpenChanged handler were dead: nothing set
        // IsOpen = true externally, and the false path in
        // ExecuteSelectedAsync was a no-op.
        return await command.Action();
    }

    private void RebuildFiltered()
    {
        FilteredCommands.Clear();
        var needle = SearchText.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            foreach (var command in _allCommands)
            {
                FilteredCommands.Add(command);
            }
        }
        else
        {
            foreach (var command in _allCommands)
            {
                if (command.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    command.Subtitle.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredCommands.Add(command);
                }
            }
        }
        SelectedIndex = 0;
    }
}
