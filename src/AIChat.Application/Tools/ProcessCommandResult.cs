namespace AIChat.Application.Tools;

internal sealed record ProcessCommandResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);
