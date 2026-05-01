using AIChat.Domain.Chat;
using AIChat.Domain.Context;

namespace AIChat.Domain.Projects;

// Project-level container. Later Agent features can attach files, commands, and
// tool permissions here without mixing them into the chat-message model.
public sealed class ProjectWorkspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AIChat";
    public string Path { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<Conversation> Conversations { get; set; } = [];
    public List<PinnedContextItem> PinnedContext { get; set; } = [];
    public List<ProjectVerificationCommand> VerificationCommands { get; set; } = [];
}
