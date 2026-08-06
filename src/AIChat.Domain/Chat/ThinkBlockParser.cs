namespace AIChat.Domain.Chat;

// 2026-08-05: stateful parser for the ``...
// blocks that MiniMax (and other open-source-
// style models) emit inline within their
// assistant content when `thinking=enabled` (or
// `adaptive`, the M3 default). The platform
// returns the think chain as part of the same
// `content` field, not as a separate
// `reasoning_content` sibling — the user
// expects the chain to be hidden behind a
// collapsible "思考过程" section, not rendered
// as raw XML-ish text inline with the answer.
//
// The parser is stateful so it can be fed one
// streaming delta at a time without losing
// partial-tag boundaries. The OpenAI-compatible
// adapter splits each SSE chunk on arbitrary
// boundaries, so a chunk may end with `<think>`
// and the next starts with the reasoning body
// — a state-less grep would leak the opening /
// closing tag fragments into the rendered
// content.
//
// The state machine has two stable states:
//   - OutsideThink: appending to the visible
//     content (the final answer the user reads)
//   - InsideThink: appending to the hidden
//     reasoning chain
//
// On `<think>` we transition OutsideThink →
// InsideThink, splitting any partial tag from
// the chunk's tail into the visible content
// (we keep the text BEFORE the tag, drop the
// tag itself, the text AFTER the tag goes to
// reasoning). On `</think>` we go the other
// way, splitting any partial tail into the
// visible content (the final answer resumes).
// Anything that doesn't look like a tag goes to
// the current state.
public sealed class ThinkBlockParser
{
    public string VisibleContent { get; private set; } = "";
    public string Thinking { get; private set; } = "";

    public bool IsInsideThink { get; private set; }

    // The OpenAI-compatible SSE stream can split
    // a tag boundary across two chunks. The
    // parser keeps the partial tail in a buffer
    // until the next chunk confirms whether it's
    // a tag prefix or plain text. 16 chars is
    // more than enough for `` (7) +
    // a few extra characters of lookahead, and
    // the buffer drains on every non-matching
    // chunk so it doesn't grow unbounded.
    private const int MaxTagLookahead = 16;
    private string _buffer = "";

    // 2026-08-05: clear the visible-content
    // accumulator so the next Append() reports
    // the diff (the new text that arrived since
    // the last call). The agent runner reads
    // VisibleContent after every Append() to
    // push the chunk to the AI bubble — we
    // don't want the runner to see the same
    // visible text twice. Thinking is left
    // untouched; the runner reads the
    // cumulative total once per delta.
    public void ResetVisibleDelta()
    {
        VisibleContent = "";
    }

    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        _buffer += delta;

        while (_buffer.Length > 0)
        {
            var tagStart = FindTagStart(_buffer);
            if (tagStart < 0)
            {
                // No complete `` or `` in the
                // buffer. We still need to
                // hold the tail if it contains
                // a `<` that could be the start
                // of a tag (the streaming case
                // where a chunk ends with
                // "<thi" and the next chunk
                // completes "<think>"). Look
                // back from the end of the
                // buffer for the most recent
                // `<` — anything from there to
                // the tail is a candidate
                // partial tag and must wait.
                var partialOpen = _buffer.LastIndexOf('<');
                if (partialOpen < 0)
                {
                    // No `<` anywhere — the
                    // whole buffer is safe to
                    // emit. (A tag must start
                    // with `<`, so without
                    // one no partial tag can
                    // be forming.)
                    Emit(_buffer);
                    _buffer = "";
                }
                else if (_buffer.Length - partialOpen >= MaxTagLookahead)
                {
                    // The `<` is far enough
                    // from the end that no
                    // tag prefix is still
                    // possible. Emit
                    // everything up to the
                    // `<`; the `<` itself is
                    // plain text (we already
                    // established no complete
                    // tag is in the buffer).
                    Emit(_buffer[..partialOpen]);
                    _buffer = _buffer[partialOpen..];
                    // The tail still contains
                    // `<` — hold it for the
                    // next chunk.
                }
                else
                {
                    // The `<` is within
                    // MaxTagLookahead of the
                    // tail. Hold the tail from
                    // the `<` onward, emit the
                    // safe prefix.
                    Emit(_buffer[..partialOpen]);
                    _buffer = _buffer[partialOpen..];
                }
                break;
            }

            // We found a complete tag inside
            // the buffer. If it's near the
            // tail and not all of its
            // characters are in the buffer
            // (e.g. a chunk ended with
            // "<thi" and FindTagStart
            // returned -1 — handled above —
            // OR the chunk ended with
            // "<think" without the closing
            // ">"), the tag might still be
            // partial. The
            // MaxTagLookahead check ensures
            // we don't act on a tag whose
            // closing characters might not
            // have arrived yet.
            if (_buffer.Length - tagStart < MaxTagLookahead
                && tagStart + "<think>".Length > _buffer.Length)
            {
                break;
            }

            if (IsInsideThink)
            {
                // We're inside the think chain.
                // Find the close tag.
                var closeIdx = _buffer.IndexOf("</think>", tagStart, StringComparison.Ordinal);
                if (closeIdx < 0)
                {
                    // No closing tag yet. The
                    // buffer starts with reasoning
                    // content (already at position
                    // 0 if tagStart is 0, since the
                    // tag we found was `` —
                    // but actually a tagStart
                    // inside InsideThink would be
                    // the close tag). Emit
                    // everything before the close
                    // tag position as reasoning.
                    Emit(_buffer[..tagStart]);
                    _buffer = _buffer[tagStart..];
                    break;
                }
                // Flush reasoning content
                // between the current position
                // and the close tag.
                Emit(_buffer[..closeIdx]);
                _buffer = _buffer[(closeIdx + "</think>".Length)..];
                IsInsideThink = false;
            }
            else
            {
                // Outside the think chain. Find
                // the open tag.
                var openIdx = _buffer.IndexOf("<think>", tagStart, StringComparison.Ordinal);
                if (openIdx < 0)
                {
                    // tagStart must have been
                    // ``; we can't process it
                    // because the matching open
                    // tag is missing. Flush
                    // everything as plain
                    // content and clear the
                    // buffer.
                    Emit(_buffer);
                    _buffer = "";
                    break;
                }
                if (openIdx + "<think>".Length >= _buffer.Length && _buffer.Length - openIdx < MaxTagLookahead)
                {
                    // Tag opens at the end and we
                    // don't have enough text to
                    // confirm. Wait for the next
                    // chunk.
                    _buffer = _buffer[openIdx..];
                    break;
                }
                // Flush content before the open
                // tag, transition into InsideThink,
                // and reset the buffer past the
                // tag.
                if (openIdx > 0)
                {
                    Emit(_buffer[..openIdx]);
                }
                _buffer = _buffer[(openIdx + "<think>".Length)..];
                IsInsideThink = true;
            }
        }
    }

    // Force-flush any pending buffer. Called
    // when the stream ends (the [DONE] sentinel
    // or the IsCompleted delta lands) so a
    // partial tag in the tail chunk isn't lost.
    // The flushed content goes to whichever
    // state the parser is in — a truncated
    // stream that ends with a half-opened
    // `<think>` will surface the literal tag
    // (and any reasoning prefix) in the visible
    // content rather than silently dropping it.
    public void Flush()
    {
        if (_buffer.Length == 0)
        {
            return;
        }
        Emit(_buffer);
        _buffer = "";
    }

    private void Emit(string text)
    {
        if (IsInsideThink)
        {
            Thinking += text;
        }
        else
        {
            VisibleContent += text;
        }
    }

    // Returns the index of a tag start (`` or
    // ``) in `text`, or -1 if neither is
    // present. A negative result means the parser
    // can flush the safe portion of the buffer.
    private static int FindTagStart(string text)
    {
        var thinkOpen = text.IndexOf("<think>", StringComparison.Ordinal);
        var thinkClose = text.IndexOf("</think>", StringComparison.Ordinal);
        if (thinkOpen < 0) return thinkClose;
        if (thinkClose < 0) return thinkOpen;
        return Math.Min(thinkOpen, thinkClose);
    }
}
