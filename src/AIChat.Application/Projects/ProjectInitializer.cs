using System.Text;
using AIChat.Domain.Projects;

namespace AIChat.Application.Projects;

/// <summary>
/// Initializes a project by generating an AGENTS.md file that helps the AI
/// understand the project structure, tech stack, and conventions.
/// </summary>
public sealed class ProjectInitializer
{
    public IReadOnlyList<ProjectVerificationCommand> SuggestVerificationCommands(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return [];
        }

        var detection = DetectProject(projectPath);
        var commands = new List<ProjectVerificationCommand>();
        var dotnetTarget = FindDotnetVerificationTarget(projectPath);
        if (detection.TechStack.Contains("C# / .NET", StringComparer.Ordinal))
        {
            commands.Add(new ProjectVerificationCommand
            {
                Name = "构建",
                Command = "dotnet build",
                WorkingDirectory = dotnetTarget,
                TimeoutSeconds = 120,
                IsDefault = true
            });

            commands.Add(new ProjectVerificationCommand
            {
                Name = "测试",
                Command = "dotnet test",
                WorkingDirectory = dotnetTarget,
                TimeoutSeconds = 180,
                IsDefault = true
            });
        }

        return commands;
    }

    public async Task InitializeProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var agentsPath = Path.Combine(projectPath, "AGENTS.md");
        if (File.Exists(agentsPath))
        {
            return; // Don't overwrite existing file
        }

        var detection = DetectProject(projectPath);
        var content = GenerateAgentsMd(projectPath, detection);
        await File.WriteAllTextAsync(agentsPath, content, cancellationToken);
    }

    private static ProjectDetection DetectProject(string root)
    {
        var detection = new ProjectDetection();

        // .NET
        if (Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories).Length > 0 ||
            Directory.GetFiles(root, "*.sln").Length > 0)
        {
            detection.TechStack.Add("C# / .NET");
            detection.BuildCommands.Add("dotnet build");
            detection.TestCommands.Add("dotnet test");
        }

        // Node.js
        if (File.Exists(Path.Combine(root, "package.json")))
        {
            detection.TechStack.Add("Node.js");
            detection.BuildCommands.Add("npm install");
            if (File.Exists(Path.Combine(root, "tsconfig.json")))
                detection.TechStack.Add("TypeScript");
        }

        // Python
        if (File.Exists(Path.Combine(root, "pyproject.toml")) ||
            File.Exists(Path.Combine(root, "requirements.txt")) ||
            File.Exists(Path.Combine(root, "setup.py")))
        {
            detection.TechStack.Add("Python");
            detection.TestCommands.Add("pytest");
        }

        // Rust
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
        {
            detection.TechStack.Add("Rust");
            detection.BuildCommands.Add("cargo build");
            detection.TestCommands.Add("cargo test");
        }

        // Go
        if (File.Exists(Path.Combine(root, "go.mod")))
        {
            detection.TechStack.Add("Go");
            detection.BuildCommands.Add("go build ./...");
            detection.TestCommands.Add("go test ./...");
        }

        // Git
        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            detection.HasGit = true;
        }

        // Docker
        if (File.Exists(Path.Combine(root, "Dockerfile")) ||
            File.Exists(Path.Combine(root, "docker-compose.yml")))
        {
            detection.TechStack.Add("Docker");
        }

        // Scan top-level directories
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "node_modules" or "bin" or "obj" or ".vs" or ".idea")
                continue;
            detection.TopLevelDirs.Add(name);
        }

        return detection;
    }

    private static string FindDotnetVerificationTarget(string root)
    {
        var solution = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(solution))
        {
            return Path.GetFileName(solution);
        }

        var project = Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(project) ? "" : Path.GetFileName(project);
    }

    private static string GenerateAgentsMd(string projectPath, ProjectDetection detection)
    {
        var sb = new StringBuilder();
        var projectName = Path.GetFileName(projectPath);

        sb.AppendLine($"# {projectName}");
        sb.AppendLine();
        sb.AppendLine("This file is auto-generated by AIChat to help the AI agent understand this project.");
        sb.AppendLine();

        if (detection.TechStack.Count > 0)
        {
            sb.AppendLine("## Tech Stack");
            sb.AppendLine();
            foreach (var tech in detection.TechStack)
            {
                sb.AppendLine($"- {tech}");
            }
            sb.AppendLine();
        }

        if (detection.TopLevelDirs.Count > 0)
        {
            sb.AppendLine("## Directory Structure");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var dir in detection.TopLevelDirs.Order())
            {
                sb.AppendLine($"{dir}/");
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (detection.BuildCommands.Count > 0)
        {
            sb.AppendLine("## Build");
            sb.AppendLine();
            sb.AppendLine("```bash");
            foreach (var cmd in detection.BuildCommands)
            {
                sb.AppendLine(cmd);
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (detection.TestCommands.Count > 0)
        {
            sb.AppendLine("## Test");
            sb.AppendLine();
            sb.AppendLine("```bash");
            foreach (var cmd in detection.TestCommands)
            {
                sb.AppendLine(cmd);
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (detection.HasGit)
        {
            sb.AppendLine("## Git");
            sb.AppendLine();
            sb.AppendLine("This project uses Git for version control.");
            sb.AppendLine();
        }

        sb.AppendLine("## Conventions");
        sb.AppendLine();
        sb.AppendLine("- Follow existing code style and patterns in the project.");
        sb.AppendLine("- Run build and tests before committing changes.");
        sb.AppendLine("- Use meaningful commit messages that describe the change.");
        sb.AppendLine();

        return sb.ToString();
    }

    private sealed class ProjectDetection
    {
        public List<string> TechStack { get; } = [];
        public List<string> BuildCommands { get; } = [];
        public List<string> TestCommands { get; } = [];
        public List<string> TopLevelDirs { get; } = [];
        public bool HasGit { get; set; }
    }
}
