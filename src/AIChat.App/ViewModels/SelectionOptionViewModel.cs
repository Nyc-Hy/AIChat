namespace AIChat.App.ViewModels;

// Simple ID/name pair for combo boxes and segmented selections.
public sealed class SelectionOptionViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public override string ToString() => Name;
}
