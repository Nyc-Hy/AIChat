using System.Text;
using System.Text.RegularExpressions;

namespace AIChat.Application.Sources;

// Cheap, dependency-free HTML → plain-text reducer.
// Lives in AIChat.Application so the Wave 7 web-fetch
// flow doesn't need to take a dependency on HtmlAgility
// Pack / AngleSharp for a first-slice implementation.
//
// Quality bar: "good enough that the agent can find the
// page's main content". Not trying to be a full
// readability-grade extractor — a follow-up slice can
// swap in AngleSharp if the agent-loop test suite
// needs better fidelity on dense pages (Medium /
// Substack / dev.to, etc.).
//
// Approach: drop <script>, <style>, <noscript> blocks
// first (they're noise). Then drop every other tag
// but preserve <p> / <br> / <h1-6> / <li> as paragraph
// breaks (so the resulting text isn't one wall of
// words). Then collapse runs of whitespace.
public static class HtmlToText
{
    // Tags whose contents we drop entirely (not just
    // the tag itself). Match across newlines so a
    // multi-line <script> block doesn't leak its
    // body into the text.
    private static readonly Regex ScriptStyle = new(
        @"<\s*(script|style|noscript)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // HTML comments.
    private static readonly Regex Comments = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Block-level tags we want to break on. The
    // replacement is "\n\n" so the paragraph boundary
    // survives the final whitespace collapse.
    private static readonly Regex BlockTags = new(
        @"</?\s*(p|div|section|article|main|h[1-6]|li|ul|ol|tr|td|th|table|br\s*/?|hr\s*/?)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Any remaining tag. Stripped to nothing.
    private static readonly Regex AnyTag = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    // Run of whitespace (including newlines that the
    // block-tag injection added). Collapsed to a
    // single space; the final pass re-introduces
    // paragraph breaks.
    private static readonly Regex Whitespace = new(
        @"[ \t]+",
        RegexOptions.Compiled);

    private static readonly Regex Newlines = new(
        @"\n[ \t]*\n+",
        RegexOptions.Compiled);

    public static string Reduce(string html, int maxLength = 200_000)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        // 1. Drop script / style / noscript / comments
        //    before any tag stripping so we don't emit
        //    their bodies as text.
        var stage1 = ScriptStyle.Replace(html, " ");
        stage1 = Comments.Replace(stage1, " ");

        // 2. Block tags → paragraph breaks.
        var stage2 = BlockTags.Replace(stage1, "\n\n");

        // 3. Strip everything else.
        var stage3 = AnyTag.Replace(stage2, " ");

        // 4. Decode the few entities we actually see
        //    in real pages. Avoid pulling in
        //    System.Web (deprecated). The full HTML5
        //    entity list is ~250 entries; these
        //    cover the 99% case and a follow-up
        //    slice can add a comprehensive table.
        var stage4 = DecodeCommonEntities(stage3);

        // 5. Collapse runs of spaces / tabs, then
        //    collapse 3+ newlines to a single blank-
        //    line break.
        var stage5 = Whitespace.Replace(stage4, " ");
        var stage6 = Newlines.Replace(stage5, "\n\n");

        // 6. Trim each line and drop empty leading
        //    lines.
        var lines = stage6.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var joined = string.Join("\n", lines);

        // 7. Cap the length so a giant page doesn't
        //    blow up the agent's context window. The
        //    agent context builder will further trim
        //    to the available token budget; this is
        //    just a safety net.
        if (joined.Length > maxLength)
        {
            joined = joined[..maxLength] + "…";
        }
        return joined;
    }

    // Title extraction. Looks for <title>...</title> in
    // the original HTML (not the post-strip output) so
    // we can return the title even if the body was
    // truncated by maxLength. Returns "" when no title
    // tag is present — the caller falls back to the
    // URL hostname.
    private static readonly Regex TitleTag = new(
        @"<\s*title\b[^>]*>(.*?)<\s*/\s*title\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ExtractTitle(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }
        var match = TitleTag.Match(html);
        if (!match.Success)
        {
            return "";
        }
        var raw = match.Groups[1].Value;
        // Titles can have entities and inline tags
        // (rare but happens — <title>Foo &amp; Bar</title>).
        // The cheap path: strip tags then decode the
        // common entities. Good enough for the first
        // slice; a follow-up can use the same
        // entity-decoding table on the raw match.
        var stripped = AnyTag.Replace(raw, "");
        return DecodeCommonEntities(stripped).Trim();
    }

    private static string DecodeCommonEntities(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }
        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '&')
            {
                var semi = input.IndexOf(';', i);
                if (semi > 0 && semi - i <= 8)
                {
                    var entity = input[(i + 1)..semi];
                    var decoded = TryDecodeEntity(entity);
                    if (decoded is not null)
                    {
                        sb.Append(decoded);
                        i = semi;
                        continue;
                    }
                }
            }
            sb.Append(input[i]);
        }
        return sb.ToString();
    }

    private static string? TryDecodeEntity(string entity)
    {
        if (entity.Length == 0 || entity[0] != '#')
        {
            return entity.ToLowerInvariant() switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                "nbsp" => " ",
                "copy" => "©",
                "reg" => "®",
                "trade" => "™",
                "hellip" => "…",
                "mdash" => "—",
                "ndash" => "–",
                "laquo" => "«",
                "raquo" => "»",
                "ldquo" => "“",
                "rdquo" => "”",
                "lsquo" => "‘",
                "rsquo" => "’",
                "middot" => "·",
                "bull" => "•",
                _ => null,
            };
        }
        // Numeric / hex character reference. Skip
        // invalid ranges rather than throw — the
        // source page can have arbitrary entities a
        // follow-up slice can add to the table.
        if (int.TryParse(entity.AsSpan(1), out var code))
        {
            if (code >= 0 && code <= 0x10FFFF)
            {
                return char.ConvertFromUtf32(code);
            }
        }
        return null;
    }
}
