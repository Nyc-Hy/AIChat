using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Tools;

public sealed record ToolMetadata
{
    public required string ToolId { get; init; }
    public string Category { get; init; } = "通用";
    public ToolPermissionMode DefaultPermissionMode { get; init; } = ToolPermissionMode.ConfirmEachTime;
    public string GroupLabel { get; init; } = "";
}
