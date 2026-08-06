using System.Text.RegularExpressions;
using AIChat.Domain.Sources;

namespace AIChat.Application.Sources;

// Parses @-references in the composer prompt. The
// syntax the user types is `@<kind>:<id>` where `<kind>`
// is the Source Kind ("web" / "clipboard" / future
// "connector" etc.) and `<id>` is the Source.Id. The
// parser returns the list of resolved references so
// the send path can promote them to InputArtifacts
// for the agent loop.
//
// The reference text STAYS in the prompt — we don't
// strip it. The user can see what they referenced
// when they look back at the conversation; the
// agent gets the same prompt + a list of resolved
// Source bodies as InputArtifacts. Stripping would
// hide the user's intent from the model.
//
// Lookup is by id (deterministic, copy-paste safe).
// Future slice: fuzzy-match by display name prefix
// for the "I copied the title, not the id" case.
public static class SourceReferenceParser
{
    // Match @<word>:<word>. The first capture is the
    // kind discriminator (word chars only — no
    // slashes / spaces), the second is the id
    // (alphanumeric + hyphens, the Source.Id shape
    // is a Guid without hyphens but we keep the
    // pattern permissive for future ids).
    private static readonly Regex Pattern = new(
        @"@(?<kind>[a-zA-Z][a-zA-Z0-9_]*):(?<id>[A-Za-z0-9_\-]+)",
        RegexOptions.Compiled);

    public static IReadOnlyList<SourceReference> Parse(
        string prompt,
        IReadOnlyList<Source> availableSources)
    {
        if (string.IsNullOrEmpty(prompt) || availableSources.Count == 0)
        {
            return [];
        }

        // Build an id → Source lookup once so the
        // match loop is O(1) per match. Two source
        // kinds can share an id (impossible in
        // practice — Source.Id is a Guid — but the
        // parser should still pick the right one).
        var byId = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in availableSources)
        {
            byId[source.Id] = source;
        }

        var result = new List<SourceReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Pattern.Matches(prompt))
        {
            var kind = match.Groups["kind"].Value;
            var id = match.Groups["id"].Value;
            if (!byId.TryGetValue(id, out var source))
            {
                // Id not found in the registry — the
                // user typed a stale reference. Skip
                // it silently; the next slice can
                // surface a toast ("unknown
                // @reference") on the send path.
                continue;
            }
            if (!string.Equals(source.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                // Kind mismatch (e.g. the user wrote
                // "@web:abc" but the id is a
                // clipboard Source). Treat as a
                // user error and skip — surfacing
                // this lets the user notice the
                // typo without crashing the run.
                continue;
            }
            // A user might paste the same reference
            // twice (e.g. once in the prompt, once
            // via the "Insert" button); dedupe by
            // id so we don't attach the same body
            // twice.
            if (!seen.Add(id))
            {
                continue;
            }
            result.Add(new SourceReference(
                source,
                match.Index,
                match.Length));
        }
        return result;
    }

    // Build the @-reference string a UI affordance
    // (the Sources row's "Insert to composer" button,
    // a future slash-command autocompleter) inserts
    // when the user picks a Source. Kept here so the
    // syntax is owned by the parser — a follow-up
    // slice that wants to change the syntax (e.g.
    // drop the colon, switch to brackets) only
    // changes one place.
    public static string FormatReference(Source source) =>
        $"@{source.Kind}:{source.Id}";
}

public sealed record SourceReference(Source Source, int StartIndex, int Length);
