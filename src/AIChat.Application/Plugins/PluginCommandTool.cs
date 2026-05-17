using System.Text.Json;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Plugins;

public sealed class PluginCommandTool : IAgentTool
{
    private readonly PluginManifest _plugin;
    private readonly PluginToolManifest _tool;

    public PluginCommandTool(PluginManifest plugin, PluginToolManifest tool)
    {
        _plugin = plugin;
        _tool = tool;
        Risk = ParseRisk(tool.Risk);
        Metadata = new ToolMetadata
        {
            ToolId = tool.Id,
            Category = string.IsNullOrWhiteSpace(tool.Category) ? "插件" : tool.Category,
            GroupLabel = string.IsNullOrWhiteSpace(tool.GroupLabel) ? $"插件：{plugin.Name}" : tool.GroupLabel,
            DefaultPermissionMode = Risk == AgentToolRisk.ReadOnly
                ? ToolPermissionMode.AutoReadOnly
                : ToolPermissionMode.ConfirmEachTime
        };
        Definition = new ChatToolDefinition
        {
            Name = tool.Id,
            Description = $"[{plugin.Name}] {tool.Description}",
            ParametersJson = string.IsNullOrWhiteSpace(tool.ParametersJson) ? "{}" : tool.ParametersJson
        };
    }

    public string Id => _tool.Id;
    public AgentToolRisk Risk { get; }
    public ToolMetadata Metadata { get; }
    public ChatToolDefinition Definition { get; }

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var command = BuildCommand(argumentsJson, context);
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"运行插件工具：{_plugin.Name}/{_tool.Name}",
            PreviewText = $"{command.Executable} {string.Join(" ", command.Arguments)}\ncwd={command.WorkingDirectory}"
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var command = BuildCommand(argumentsJson, context);
        if (string.IsNullOrWhiteSpace(command.Executable))
        {
            return Error("插件命令缺少 executable。");
        }

        if (!Directory.Exists(command.WorkingDirectory))
        {
            return Error($"插件工作目录不存在：{command.WorkingDirectory}");
        }

        var result = await ProcessCommand.RunAsync(
            command.Executable,
            command.Arguments,
            command.WorkingDirectory,
            command.TimeoutSeconds,
            cancellationToken);
        var content = JsonSerializer.Serialize(new
        {
            plugin = _plugin.Id,
            tool = _tool.Id,
            command = command.Executable,
            arguments = command.Arguments,
            workingDirectory = command.WorkingDirectory,
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            stdout = Truncate(result.Stdout, command.MaxOutputChars),
            stderr = Truncate(result.Stderr, command.MaxOutputChars),
            output = Truncate(CombineOutput(result), command.MaxOutputChars)
        });

        return new AgentToolResult
        {
            ToolName = Id,
            Content = content,
            IsError = result.ExitCode != 0 || result.TimedOut,
            FailureReason = result.ExitCode == 0 && !result.TimedOut ? "" : $"Plugin command exited with {result.ExitCode}."
        };
    }

    private PluginCommand BuildCommand(string argumentsJson, AgentToolContext context)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var variables = FlattenArguments(args);
        variables["project_path"] = context.ProjectPath;
        variables["plugin_path"] = _plugin.DirectoryPath;
        variables["plugin_id"] = _plugin.Id;
        variables["tool_id"] = _tool.Id;

        var workingDirectory = ExpandTemplate(_tool.Command.WorkingDirectory, variables);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = _plugin.DirectoryPath;
        }

        workingDirectory = ResolvePluginPath(
            workingDirectory,
            _plugin.DirectoryPath,
            context.ProjectPath,
            allowProjectPath: true);

        var executable = ExpandTemplate(_tool.Command.Executable, variables);
        executable = ResolveExecutable(executable, _plugin.DirectoryPath);
        var commandArgs = _tool.Command.Arguments
            .Select(argument => ExpandTemplate(argument, variables))
            .ToList();
        return new PluginCommand(
            executable,
            commandArgs,
            workingDirectory,
            Math.Clamp(_tool.Command.TimeoutSeconds <= 0 ? 30 : _tool.Command.TimeoutSeconds, 1, 120),
            Math.Clamp(_tool.Command.MaxOutputChars <= 0 ? 12000 : _tool.Command.MaxOutputChars, 1, 40000));
    }

    private static Dictionary<string, string> FlattenArguments(JsonElement args)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args.ValueKind != JsonValueKind.Object)
        {
            return variables;
        }

        foreach (var property in args.EnumerateObject())
        {
            variables[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                _ => property.Value.GetRawText()
            };
        }

        return variables;
    }

    private static string ExpandTemplate(string template, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = template;
        foreach (var (key, value) in variables)
        {
            expanded = expanded.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static string ResolveExecutable(string executable, string pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return "";
        }

        if (!Path.IsPathRooted(executable) && LooksLikeRelativePath(executable))
        {
            var resolved = Path.GetFullPath(Path.Combine(pluginDirectory, executable));
            EnsureInside(resolved, pluginDirectory, "插件 executable 必须位于插件目录内，或使用 PATH 中的命令名。");
            return resolved;
        }

        return executable;
    }

    private static string ResolvePluginPath(
        string path,
        string pluginDirectory,
        string projectPath,
        bool allowProjectPath)
    {
        var baseDirectory = pluginDirectory;
        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));

        if (IsInside(resolved, pluginDirectory))
        {
            return resolved;
        }

        if (allowProjectPath && IsInside(resolved, projectPath))
        {
            return resolved;
        }

        throw new InvalidOperationException("插件工作目录必须位于插件目录或当前项目目录内。");
    }

    private static bool LooksLikeRelativePath(string executable)
    {
        return executable.Contains('/') ||
               executable.Contains('\\') ||
               executable.StartsWith(".", StringComparison.Ordinal);
    }

    private static void EnsureInside(string path, string root, string message)
    {
        if (!IsInside(path, root))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentToolRisk ParseRisk(string risk)
    {
        return risk.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read_only" or "read-only" or "read" => AgentToolRisk.ReadOnly,
            "write" or "mutation" => AgentToolRisk.Write,
            _ => AgentToolRisk.Shell
        };
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + $"\n...[truncated {value.Length - maxChars} chars]";
    }

    private static string CombineOutput(ProcessCommandResult result)
    {
        var combined = result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n[stderr]\n" + result.Stderr);
        return string.IsNullOrWhiteSpace(combined)
            ? "(插件命令执行完成，但没有输出。)"
            : combined;
    }

    private AgentToolResult Error(string content) => new()
    {
        ToolName = Id,
        Content = content,
        IsError = true,
        FailureReason = content
    };

    private sealed record PluginCommand(
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int TimeoutSeconds,
        int MaxOutputChars);
}
