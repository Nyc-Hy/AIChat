namespace AIChat.Application.Tools;

internal sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);
