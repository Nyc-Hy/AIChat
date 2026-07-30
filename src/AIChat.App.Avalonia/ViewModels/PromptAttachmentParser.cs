using System.IO;
using System.Text.RegularExpressions;

namespace AIChat.App.Avalonia.ViewModels;

// Parses @path tokens out of a user prompt and resolves them to file
// contents. The user types something like
//
//     "what does @src/Program.cs do?"
//
// and the parser extracts 'src/Program.cs' (relative to the project
// root if available, else the user's home directory) and produces a
// Result the host can use to:
//   1. Drop a system bubble in the activity feed so the user can see
//      what got attached (full path + content preview)
//   2. Inject the file content as context for the agent
//
// Bad paths (file not found, too big, etc.) are surfaced as warnings
// in the Result so the user gets feedback rather than a silent
// failure.
public static class PromptAttachmentParser
{
    public sealed record Attachment(string ResolvedPath, string Content, int ByteCount);
    public sealed record Warning(string OriginalToken, string Message);
    public sealed record Result(string CleanPrompt, IReadOnlyList<Attachment> Attachments, IReadOnlyList<Warning> Warnings);

    // Max size we'll inline (16KB). Larger files are skipped with a
    // warning so the conversation context doesn't blow up. The agent
    // already has a read_file tool for anything bigger.
    private const int MaxInlineBytes = 16 * 1024;

    // Match @<token> where the token is at least one char and contains
    // no whitespace / quote / comma / paren. Stops at typical
    // sentence-ending punctuation.
    private static readonly Regex TokenRegex = new(
        @"@(?<path>[^\s""',()\[\]]+)",
        RegexOptions.Compiled);

    public static Result Parse(string prompt, string? projectRoot)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            return new Result(prompt, [], []);
        }

        var attachments = new List<Attachment>();
        var warnings = new List<Warning>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleanPrompt = TokenRegex.Replace(prompt, match =>
        {
            var token = match.Groups["path"].Value;
            var resolved = ResolvePath(token, projectRoot);

            if (!File.Exists(resolved))
            {
                warnings.Add(new Warning(token, "文件不存在"));
                return match.Value;
            }

            if (!seen.Add(resolved))
            {
                // Same file referenced twice — drop the second token
                // but don't warn.
                return string.Empty;
            }

            var info = new FileInfo(resolved);
            if (info.Length > MaxInlineBytes)
            {
                warnings.Add(new Warning(
                    token,
                    $"文件过大 ({info.Length / 1024} KB > {MaxInlineBytes / 1024} KB),请用 read_file 工具"));
                return match.Value;
            }

            try
            {
                var content = File.ReadAllText(resolved);
                attachments.Add(new Attachment(resolved, content, (int)info.Length));
                return string.Empty; // strip the @token from the prompt
            }
            catch (Exception ex)
            {
                warnings.Add(new Warning(token, $"读取失败: {ex.Message}"));
                return match.Value;
            }
        });

        // Collapse double spaces left behind by removed tokens.
        cleanPrompt = Regex.Replace(cleanPrompt, @"\s{2,}", " ").Trim();

        return new Result(cleanPrompt, attachments, warnings);
    }

    private static string ResolvePath(string token, string? projectRoot)
    {
        if (Path.IsPathRooted(token) || token.StartsWith("~"))
        {
            return ExpandHome(token);
        }
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            return Path.Combine(projectRoot, token);
        }
        return ExpandHome(token);
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[1..].TrimStart('/'));
        }
        return path;
    }
}
