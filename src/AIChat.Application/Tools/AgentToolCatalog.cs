namespace AIChat.Application.Tools;

public sealed class AgentToolCatalog
{
    private readonly IReadOnlyList<IAgentTool> _tools;

    public AgentToolCatalog(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToList();
    }

    public IReadOnlyList<IAgentTool> All => _tools;

    public IReadOnlyList<IAgentTool> ResolveEnabled(IEnumerable<string> enabledToolIds)
    {
        var enabled = enabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _tools.Where(tool => enabled.Contains(tool.Id)).ToList();
    }

    public IAgentTool? Find(string nameOrId)
    {
        return _tools.FirstOrDefault(tool =>
            string.Equals(tool.Id, nameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tool.Definition.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    public static AgentToolCatalog CreateDefault()
    {
        return new AgentToolCatalog(
        [
            new ListFilesTool(),
            new ReadFileTool(),
            new SearchTextTool(),
            new WriteFileTool(),
            new EditFileTool(),
            new ApplyPatchTool(),
            new GitStatusTool(),
            new GitDiffTool(),
            new RunBuildTool(),
            new RunTestTool(),
            new ShellCommandTool()
        ]);
    }
}
