namespace AIChat.Application.Artifacts;

public sealed class InputArtifactCleanupOptions
{
    public int MaxArtifactsPerConversation { get; init; } = 20;
    public int MaxProjectLevelArtifacts { get; init; } = 20;
}
