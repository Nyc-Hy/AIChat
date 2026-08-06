namespace AIChat.Application.Tools;

internal static class GitCommand
{
    internal static async Task<GitCommandResult> RunAsync(
        string? projectPath,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var root = ProjectPathGuard.ResolveInsideProject(projectPath, "");
        var result = await ProcessCommand.RunAsync("git", arguments, root, timeoutSeconds, cancellationToken);
        return new GitCommandResult(result.ExitCode, result.Stdout, result.Stderr, result.TimedOut);
    }
}
