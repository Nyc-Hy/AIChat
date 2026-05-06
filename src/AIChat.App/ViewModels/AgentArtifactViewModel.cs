using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentArtifactViewModel
{
    private readonly AgentArtifact _artifact;

    public AgentArtifactViewModel(AgentArtifact artifact)
    {
        _artifact = artifact;
    }

    public string Id => _artifact.Id;
    public string ToolName => string.IsNullOrWhiteSpace(_artifact.ToolName) ? "未知工具" : _artifact.ToolName;
    public string Kind => string.IsNullOrWhiteSpace(_artifact.Kind) ? "tool_result" : _artifact.Kind;
    public string Summary => string.IsNullOrWhiteSpace(_artifact.Summary) ? "无摘要" : _artifact.Summary;
    public string Content => _artifact.Content;
    public int ContentLength => _artifact.Content.Length;
    public string ContentLengthText => $"{ContentLength} 字符";
    public string CreatedText => _artifact.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
