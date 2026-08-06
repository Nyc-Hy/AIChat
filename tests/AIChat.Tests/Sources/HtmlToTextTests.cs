using AIChat.Application.Sources;

namespace AIChat.Tests.Sources;

// HtmlToText is the cheap HTML reducer the Wave 7
// web-fetch path uses to make a fetched page
// agent-readable. The tests focus on the contracts
// the agent loop actually relies on: script / style
// bodies don't leak, paragraph boundaries survive,
// entities decode, and a missing title falls back
// cleanly.
public class HtmlToTextTests
{
    [Fact]
    public void Reduce_PlainText_Unchanged()
    {
        var html = "<p>Hello, world!</p>";
        var text = HtmlToText.Reduce(html);
        Assert.Contains("Hello, world!", text);
    }

    [Fact]
    public void Reduce_DropsScriptBodies()
    {
        // A real-world example: analytics scripts
        // inline JSON-shaped strings that would
        // otherwise leak into the agent's context.
        var html = """
            <p>Before</p>
            <script>window.__INITIAL_STATE__ = {"user":"secret","token":"x"}</script>
            <p>After</p>
            """;
        var text = HtmlToText.Reduce(html);
        Assert.Contains("Before", text);
        Assert.Contains("After", text);
        Assert.DoesNotContain("__INITIAL_STATE__", text);
        Assert.DoesNotContain("secret", text);
    }

    [Fact]
    public void Reduce_DropsStyleBodies()
    {
        var html = """
            <style>body { color: red; }</style>
            <p>Visible</p>
            """;
        var text = HtmlToText.Reduce(html);
        Assert.DoesNotContain("color:", text);
        Assert.DoesNotContain("red", text);
        Assert.Contains("Visible", text);
    }

    [Fact]
    public void Reduce_DropsComments()
    {
        var html = """
            <p>Public</p>
            <!-- TODO: remove before ship -->
            <p>Also public</p>
            """;
        var text = HtmlToText.Reduce(html);
        Assert.DoesNotContain("TODO", text);
        Assert.DoesNotContain("remove before ship", text);
    }

    [Fact]
    public void Reduce_PreservesParagraphBoundaries()
    {
        var html = "<p>First paragraph.</p><p>Second paragraph.</p><p>Third paragraph.</p>";
        var text = HtmlToText.Reduce(html);
        var lines = text.Split('\n').Where(l => l.Length > 0).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal("First paragraph.", lines[0]);
        Assert.Equal("Second paragraph.", lines[1]);
        Assert.Equal("Third paragraph.", lines[2]);
    }

    [Fact]
    public void Reduce_HandlesHeadings()
    {
        var html = "<h1>Title</h1><p>Body.</p><h2>Section</h2><p>More body.</p>";
        var text = HtmlToText.Reduce(html);
        Assert.Contains("Title", text);
        Assert.Contains("Body.", text);
        Assert.Contains("Section", text);
        Assert.Contains("More body.", text);
    }

    [Fact]
    public void Reduce_DecodesCommonEntities()
    {
        var html = "<p>Foo &amp; Bar &lt;3 &quot;baz&quot; &copy; 2026</p>";
        var text = HtmlToText.Reduce(html);
        Assert.Contains("Foo & Bar", text);
        Assert.Contains("<3", text);
        Assert.Contains("\"baz\"", text);
        Assert.Contains("©", text);
    }

    [Fact]
    public void Reduce_CollapsesRunsOfWhitespace()
    {
        var html = "<p>Lots    of\n\n\nwhitespace   here.</p>";
        var text = HtmlToText.Reduce(html);
        // No runs of 3+ spaces survive.
        Assert.DoesNotContain("    ", text);
        Assert.DoesNotContain("\n\n\n", text);
    }

    [Fact]
    public void Reduce_HandlesListItems()
    {
        var html = "<ul><li>One</li><li>Two</li><li>Three</li></ul>";
        var text = HtmlToText.Reduce(html);
        Assert.Contains("One", text);
        Assert.Contains("Two", text);
        Assert.Contains("Three", text);
    }

    [Fact]
    public void Reduce_TruncatesLongPages()
    {
        // 250KB of "word " → reducer must cap at
        // the configured length to keep the agent
        // context bounded. The default cap is
        // 200_000.
        var big = string.Concat(Enumerable.Repeat("word ", 50_000));
        var html = $"<p>{big}</p>";
        var text = HtmlToText.Reduce(html);
        Assert.True(text.Length <= 200_001,
            $"Reduced text length {text.Length} should be <= 200_001 (cap is 200_000 + ellipsis)");
    }

    [Fact]
    public void Reduce_EmptyHtml_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToText.Reduce(""));
    }

    [Fact]
    public void ExtractTitle_BasicTitle()
    {
        Assert.Equal("My Article", HtmlToText.ExtractTitle("<html><head><title>My Article</title></head>"));
    }

    [Fact]
    public void ExtractTitle_DecodesEntities()
    {
        Assert.Equal("Foo & Bar", HtmlToText.ExtractTitle("<title>Foo &amp; Bar</title>"));
    }

    [Fact]
    public void ExtractTitle_StripsInlineTags()
    {
        // Some CMSes put <span> / <em> inside <title>.
        // We strip the tag but keep the inner text
        // — the title "Featured Article" is the
        // agent-readable surface, the wrapping
        // <em> is just presentational.
        Assert.Equal("Featured Article", HtmlToText.ExtractTitle("<title>Featured <em>Article</em></title>"));
    }

    [Fact]
    public void ExtractTitle_MissingTitle_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToText.ExtractTitle("<html><body>no title here</body></html>"));
    }

    [Fact]
    public void ExtractTitle_EmptyHtml_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToText.ExtractTitle(""));
    }

    [Fact]
    public void Reduce_MultilineScript_DoesNotLeak()
    {
        // A multi-line <script> block (the cheap
        // regex must use Singleline / IgnoreCase
        // for the body match). The body should not
        // appear in the reduced text.
        var html = """
            <script>
              function f() {
                return "leaked-string";
              }
            </script>
            <p>Visible</p>
            """;
        var text = HtmlToText.Reduce(html);
        Assert.DoesNotContain("leaked-string", text);
        Assert.DoesNotContain("function f", text);
    }
}
