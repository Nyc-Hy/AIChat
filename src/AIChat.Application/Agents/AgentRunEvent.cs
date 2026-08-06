using AIChat.Domain.Chat;
using AIChat.Application.Tools;

namespace AIChat.Application.Agents;

public sealed class AgentRunEvent
{
    public required AgentRunEventType Type { get; init; }
    public string Content { get; init; } = "";
    public string RawJson { get; init; } = "";
    public ChatToolCall? ToolCall { get; init; }
    public AgentToolPreview? ToolPreview { get; init; }
    public AgentToolResult? ToolResult { get; init; }
    public string SessionAllowedToolId { get; init; } = "";
    // 2026-08-05: token usage from the final chunk
    // of a streaming response. Forwarded to the
    // harness so the runner can surface the cache
    // hit rate in the activity feed / status bar.
    // Set on the ChatDelta whose IsCompleted=true
    // OR on the usage-only final chunk that some
    // providers emit between the last content
    // delta and the [DONE] sentinel. Null on
    // intermediate chunks.
    public ChatUsage? Usage { get; init; }
}
