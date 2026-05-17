namespace AIChat.Application.Plugins;

public static class PluginSkillLoader
{
    public const int MaxSkillChars = 12000;

    public static async Task<IReadOnlyList<PluginSkill>> LoadAsync(
        IReadOnlyList<PluginManifest> manifests,
        CancellationToken cancellationToken = default)
    {
        var skills = new List<PluginSkill>();
        foreach (var manifest in manifests)
        {
            foreach (var skill in manifest.Skills.Where(item => item.Enabled))
            {
                var path = ResolveSkillPath(manifest.DirectoryPath, skill.Path);
                if (!File.Exists(path))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(path, cancellationToken);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                skills.Add(new PluginSkill(
                    manifest.Id,
                    skill.Id,
                    string.IsNullOrWhiteSpace(skill.Name) ? skill.Id : skill.Name,
                    skill.Description,
                    Truncate(content.Trim(), MaxSkillChars),
                    path));
            }
        }

        return skills;
    }

    internal static string ResolveSkillPath(string pluginDirectory, string skillPath)
    {
        var resolved = Path.IsPathRooted(skillPath)
            ? Path.GetFullPath(skillPath)
            : Path.GetFullPath(Path.Combine(pluginDirectory, skillPath));
        if (!IsInside(resolved, pluginDirectory))
        {
            throw new InvalidOperationException("Skill 文件必须位于插件目录内。");
        }

        return resolved;
    }

    private static bool IsInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars
            ? value
            : value[..maxChars] + $"\n\n...[plugin skill truncated {value.Length - maxChars} chars]";
    }
}
