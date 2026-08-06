using System.Text.Json.Serialization;
using AIChat.Domain.Artifacts;

namespace AIChat.Domain.Sources;

// Wave 7 (parity plan §7 Wave 7) first slice: a Source
// is one row in the "数据源" / "Sources" panel — a
// snapshot of external content the user has captured
// (clipboard text, a fetched URL, a connector import) so
// the agent can reference it in a later run.
//
// The first-slice Source is intentionally text-only. Image
// snapshots already flow through the pending-attachments
// strip on the composer; URL fetching + connector imports
// are follow-up slices that re-use the same domain
// shape (Kind discriminates them, Content is the captured
// payload, Metadata is per-kind extension data).
public sealed class Source
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    // Free-form kind discriminator — "clipboard" /
    // "web" / "plugin" / "connector" / … the XAML
    // drives the icon glyph off this string instead
    // of a hard-coded enum so follow-up kinds plug
    // in without a UI change.
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    // What the user sees in the Sources list. For
    // clipboard: the first line of the captured
    // text, truncated; for web: the page title; for
    // a connector: the source's display name.
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // Full captured text. For long clipboard
    // captures the display name is the first line
    // and the full body lives here.
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("capturedAt")]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // 1.0.1 (Wave 7 third slice): the MimeType the
    // agent-loop's InputArtifactCreateRequest wants.
    // For "text" / "markdown" / "web fetch" Sources the
    // body is plain UTF-8 text; the mime is "text/plain"
    // for everything that isn't a pre-classified image.
    // The paste-image path already sets the right
    // mime on the PendingAttachment; this helper
    // mirrors the same rule for the @-reference path
    // so the agent sees a consistent artifact shape
    // regardless of where the bytes came from.
    public string MimeTypeOrFallback(InputArtifactKind kind)
    {
        return kind switch
        {
            InputArtifactKind.Image => "image/png",
            InputArtifactKind.Screenshot => "image/png",
            _ => "text/plain",
        };
    }
}
