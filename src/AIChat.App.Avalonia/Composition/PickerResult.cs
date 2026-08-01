namespace AIChat.App.Avalonia.Composition;

// Three-state result from a file-system dialog so callers can
// distinguish "user picked a folder" from "user hit Cancel" from
// "the dialog failed to open (sandbox / no TopLevel / OS denial)".
// Before this, the picker returned string? — a null could mean
// cancel OR failure, and the caller had no way to surface a real
// error to the user, so macOS sandbox denials looked like the
// user just cancelled.
public abstract record PickerResult
{
    public sealed record Picked(string Path) : PickerResult;
    public sealed record Cancelled : PickerResult;
    public sealed record Failed(string Reason) : PickerResult;
}
