namespace AIChat.Domain.Projects;

public sealed class ProjectVerificationCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public bool IsDefault { get; set; }
}
