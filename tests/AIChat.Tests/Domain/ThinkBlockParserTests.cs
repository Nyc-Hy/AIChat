using AIChat.Domain.Chat;

namespace AIChat.Tests.Domain;

// 2026-08-05: the ThinkBlockParser powers the
// new "💭 思考过程" collapsible section in the
// AI bubble. It must:
//   1. Strip `` blocks from the visible
//      content (the answer) and put the chain
//      in Thinking.
//   2. Handle streaming chunks where the tag
//      boundary falls mid-delta (e.g. one
//      chunk ends with "<think>" and the next
//      starts with the reasoning body).
//   3. Flush the pending buffer on
//      [DONE] so a truncated stream doesn't
//      silently drop the literal tag text.
//
// The first test (no think) is the regression
// guard for "M2.7 doesn't emit think blocks, the
// AI bubble still renders the answer verbatim".
public class ThinkBlockParserTests
{
    [Fact]
    public void Append_NoThinkBlocks_AllContentGoesToVisible()
    {
        var parser = new ThinkBlockParser();

        parser.Append("Hello there, this is a normal answer.");

        Assert.Equal("Hello there, this is a normal answer.", parser.VisibleContent);
        Assert.Equal("", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Append_SimpleThinkBlock_SplitsCorrectly()
    {
        // The MiniMax curl probe shape: `` at
        // the start, plain answer after ``.
        var parser = new ThinkBlockParser();

        parser.Append("<think>The user just said</think>Hello there.");

        Assert.Equal("Hello there.", parser.VisibleContent);
        Assert.Equal("The user just said", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Append_ThinkBlockAtEnd_KeepsContentInThinking()
    {
        // Some models emit the answer first and
        // the post-hoc reasoning after (for
        // summarization / verification). The
        // parser must leave the final state as
        // IsInsideThink = true so the section
        // remains visible.
        var parser = new ThinkBlockParser();

        parser.Append("Here's the answer.<think>Now I should verify the result.");

        Assert.Equal("Here's the answer.", parser.VisibleContent);
        Assert.Equal("Now I should verify the result.", parser.Thinking);
        Assert.True(parser.IsInsideThink);
    }

    [Fact]
    public void Append_TagArrivesInSecondChunk_BuffersAcrossDeltas()
    {
        // The OpenAI SSE stream can split a tag
        // across two chunks. The first delta is
        // plain text + the opening `<`; the
        // second delta completes the tag and
        // the chain. The parser must hold the
        // partial tail until the next chunk
        // confirms it's a tag prefix.
        var parser = new ThinkBlockParser();

        // First delta ends with "<" — a single
        // opening angle bracket, the most
        // aggressive possible tag-prefix split.
        parser.Append("Hello world.<");
        Assert.Equal("Hello world.", parser.VisibleContent);
        Assert.Equal("", parser.Thinking);

        parser.ResetVisibleDelta();

        // Second delta completes the tag and
        // carries the full think chain.
        parser.Append("think>Reasoning here.</think>And the answer.");
        Assert.Equal("And the answer.", parser.VisibleContent);
        Assert.Equal("Reasoning here.", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Append_RealMiniMaxM3Output_ParsesCorrectly()
    {
        // The actual MiniMax M3 streaming
        // output (verified via curl probe
        // 2026-08-05): `` at the start,
        // newline-terminated chain, ``
        // closes, then a newline-terminated
        // answer. This is the regression
        // guard for "the live M3 model feeds
        // the parser and the AI bubble
        // renders the answer without the
        // chain text leaking in".
        //
        // The model emits a leading newline
        // after `` (the "gap" between
        // close tag and answer). The parser
        // passes that through to
        // VisibleContent verbatim; markdown
        // renderers (and the
        // MarkdownTextBlock control) trim a
        // leading blank line, so the
        // rendered bubble shows the answer
        // without a visible blank row. The
        // parser doesn't try to strip
        // whitespace itself — that's a
        // presentation concern, not a
        // parsing concern.
        var parser = new ThinkBlockParser();

        parser.Append("<think>\nThe user just said hi.\n</think>\nHello there.\n");

        Assert.Equal("\nHello there.\n", parser.VisibleContent);
        Assert.Equal("\nThe user just said hi.\n", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Append_VisibleAndThinkingUpdatesTogether_DuringStream()
    {
        // The agent runner reads VisibleContent
        // (delta) and Thinking (cumulative)
        // after every Append() to push the new
        // chunk to the AI bubble. The runner
        // expects:
        //   - VisibleContent to be the diff
        //     (the chunk that just arrived)
        //   - Thinking to be the full chain
        //     so far (cumulative, so the
        //     "💭 思考过程" header can update
        //     mid-stream)
        var parser = new ThinkBlockParser();

        parser.Append("<think>Reasoning part 1. ");
        Assert.Equal("", parser.VisibleContent);
        Assert.Equal("Reasoning part 1. ", parser.Thinking);

        parser.ResetVisibleDelta();

        parser.Append("More reasoning.</think>The answer.");
        Assert.Equal("The answer.", parser.VisibleContent);
        Assert.Equal("Reasoning part 1. More reasoning.", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Append_PartialThinkTagInTail_BuffersUntilConfirmed()
    {
        // The streaming case the parser was
        // built for. A chunk ends with the
        // first 3 chars of "<think>" — not
        // enough to commit. The next chunk
        // starts with the remaining 4 chars
        // ("nk>") and completes the tag.
        //
        // This split is realistic: the
        // OpenAI-compatible SSE stream breaks
        // on arbitrary byte boundaries, so a
        // token like "<think>" can land as
        // "<thi" + "nk>..." across two
        // deltas. The parser must hold the
        // partial tail in its buffer until
        // the next chunk confirms whether
        // it's a tag prefix or plain text.
        var parser = new ThinkBlockParser();

        // "Hello " is the safe prefix — it
        // contains no `<`, so the parser
        // emits it immediately. The "<thi"
        // tail is held in the buffer (within
        // the 16-char lookahead window) so
        // the tag isn't split.
        parser.Append("Hello <thi");

        // The "Hello " lands in VisibleContent;
        // the "<thi" stays buffered (not yet
        // confirmed as a tag prefix).
        Assert.Equal("Hello ", parser.VisibleContent);
        Assert.Equal("", parser.Thinking);

        parser.ResetVisibleDelta();

        // Next chunk completes the tag and
        // enters InsideThink. Once the
        // buffer sees the full "<think>" the
        // parser transitions, flushes the
        // tag, and starts feeding the
        // reasoning body to Thinking. The
        // post-</think> answer lands in
        // VisibleContent.
        parser.Append("nk>The model reasoned here.</think>And the answer.");

        // The reasoning went into Thinking;
        // the answer went into VisibleContent.
        Assert.Equal("And the answer.", parser.VisibleContent);
        Assert.Equal("The model reasoned here.", parser.Thinking);
        Assert.False(parser.IsInsideThink);
    }

    [Fact]
    public void Flush_PendingBufferAtEndOfStream_EmitsToCorrectSide()
    {
        // A truncated stream (the user closed
        // the window mid-generation) may end
        // with the buffer holding a partial
        // tag. Flush() force-emits the buffer
        // to whichever state the parser is in
        // so the user sees the raw text rather
        // than a silent loss.
        var parser = new ThinkBlockParser();
        parser.Append("Answer text.<think>incomplete reasoning");

        // Mid-stream — buffer holds the
        // partial reasoning body (no closing
        // tag).
        Assert.True(parser.IsInsideThink);

        parser.Flush();

        // Flush drains the buffer to Thinking.
        Assert.Equal("Answer text.", parser.VisibleContent);
        Assert.Equal("incomplete reasoning", parser.Thinking);
    }

    [Fact]
    public void Flush_EmptyBuffer_DoesNothing()
    {
        var parser = new ThinkBlockParser();
        parser.Append("complete answer");

        parser.Flush();

        Assert.Equal("complete answer", parser.VisibleContent);
        Assert.Equal("", parser.Thinking);
    }

    [Fact]
    public void ResetVisibleDelta_AfterAppend_StartsNextDiffFromZero()
    {
        // The agent runner calls
        // parser.VisibleContent after every
        // Append() to push the chunk to the
        // AI bubble. ResetVisibleDelta clears
        // the accumulator so the next
        // Append() reports only the new text,
        // not the cumulative total.
        var parser = new ThinkBlockParser();

        parser.Append("first chunk");
        Assert.Equal("first chunk", parser.VisibleContent);

        parser.ResetVisibleDelta();
        Assert.Equal("", parser.VisibleContent);

        parser.Append("second chunk");
        Assert.Equal("second chunk", parser.VisibleContent);
    }
}
