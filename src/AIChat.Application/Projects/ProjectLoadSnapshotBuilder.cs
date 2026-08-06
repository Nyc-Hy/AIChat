using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Application.Projects;

public static class ProjectLoadSnapshotBuilder
{
    public static ProjectLoadSnapshot Build(WorkspaceProject project, IReadOnlyList<ChatSession> sessionsForProject)
    {
        var primaryPath = TryGetPrimaryPath(project);
        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            return new ProjectLoadSnapshot(
                "健康：未设置项目路径",
                "画像：等待选择项目文件夹",
                BuildActivity(project, sessionsForProject),
                "建议：先设置项目路径，再推断验证命令和生成 AGENTS.md。");
        }

        if (!Directory.Exists(primaryPath))
        {
            return new ProjectLoadSnapshot(
                "健康：项目路径不存在",
                $"画像：{primaryPath}",
                BuildActivity(project, sessionsForProject),
                "建议：重新选择项目文件夹，避免 Agent 在错误目录运行工具。");
        }

        var agentsExists = File.Exists(Path.Combine(primaryPath, "AGENTS.md"));
        var verification = project.VerificationCommands.Count;
        var healthParts = new List<string>
        {
            "健康：路径可用",
            agentsExists ? "AGENTS.md 已就绪" : "缺少 AGENTS.md",
            verification > 0 ? $"{verification} 个验证命令" : "无验证命令"
        };

        return new ProjectLoadSnapshot(
            string.Join(" · ", healthParts),
            BuildProfile(primaryPath),
            BuildActivity(project, sessionsForProject),
            BuildRecommendation(agentsExists, verification));
    }

    private static string? TryGetPrimaryPath(WorkspaceProject project)
    {
        try
        {
            return project.PrimaryPath;
        }
        catch (InvalidOperationException)
        {
            // 没 primary / folder 漂移 → 当成 "未设置" 处理
            return null;
        }
    }

    private static string BuildProfile(string projectPath)
    {
        var tech = DetectTech(projectPath);
        var topDirs = Directory.GetDirectories(projectPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !IsIgnoredDirectory(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var anchors = FindAnchorFiles(projectPath);

        var techText = tech.Count == 0 ? "未知技术栈" : string.Join(", ", tech);
        var dirText = topDirs.Count == 0 ? "无明显顶层目录" : string.Join(", ", topDirs);
        var anchorText = anchors.Count == 0 ? "无 README/solution 等锚点文件" : string.Join(", ", anchors);
        return $"画像：{techText} · 目录：{dirText} · 锚点：{anchorText}";
    }

    private static string BuildActivity(WorkspaceProject project, IReadOnlyList<ChatSession> sessionsForProject)
    {
        var runs = sessionsForProject.SelectMany(session => session.AgentRuns).ToList();
        var needsChanges = runs.Count(run => run.AcceptanceStatus == AgentRunAcceptanceStatus.NeedsChanges);
        var unreviewed = runs.Count(run => run.AcceptanceStatus == AgentRunAcceptanceStatus.Unreviewed && run.Status == AgentRunStatus.Completed);
        var memoryCount = project.Memories.Count;
        var pendingMemoryCount = project.PendingMemories.Count;
        var lastRun = runs.OrderByDescending(run => run.StartedAt).FirstOrDefault();
        var lastRunText = lastRun is null
            ? "暂无 Agent Run"
            : $"最近：{FormatStatus(lastRun.Status)} · {Trim(lastRun.Goal, 42)}";
        var pendingText = pendingMemoryCount == 0 ? "" : $" · {pendingMemoryCount} 条待确认记忆";
        return $"活动：{sessionsForProject.Count} 个对话 · {runs.Count} 次运行 · {memoryCount} 条记忆{pendingText} · {needsChanges} 个需修改 · {unreviewed} 个未验收 · {lastRunText}";
    }

    private static string BuildRecommendation(bool agentsExists, int verificationCount)
    {
        if (!agentsExists)
        {
            return "建议：生成或补充 AGENTS.md，让 Agent 先读到项目规则。";
        }

        if (verificationCount == 0)
        {
            return "建议：添加项目验证命令，写入任务才能自动验收。";
        }

        return "建议：项目已具备基础上下文和验证入口，可以开始端到端 Agent 任务。";
    }

    private static List<string> DetectTech(string root)
    {
        var result = new List<string>();
        if (Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Length > 0 ||
            Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Length > 0 ||
            Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories).Length > 0)
        {
            result.Add(".NET");
        }

        if (File.Exists(Path.Combine(root, "package.json")))
        {
            result.Add("Node.js");
        }

        if (File.Exists(Path.Combine(root, "pyproject.toml")) ||
            File.Exists(Path.Combine(root, "requirements.txt")) ||
            File.Exists(Path.Combine(root, "setup.py")))
        {
            result.Add("Python");
        }

        if (File.Exists(Path.Combine(root, "Cargo.toml")))
        {
            result.Add("Rust");
        }

        if (File.Exists(Path.Combine(root, "go.mod")))
        {
            result.Add("Go");
        }

        if (File.Exists(Path.Combine(root, "Dockerfile")) ||
            File.Exists(Path.Combine(root, "docker-compose.yml")))
        {
            result.Add("Docker");
        }

        return result;
    }

    private static List<string> FindAnchorFiles(string root)
    {
        var names = new[]
        {
            "AGENTS.md",
            "README.md",
            "package.json",
            "pyproject.toml",
            "Cargo.toml",
            "go.mod"
        };
        var anchors = names.Where(name => File.Exists(Path.Combine(root, name))).ToList();
        anchors.AddRange(Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Select(Path.GetFileName)!);
        anchors.AddRange(Directory.GetFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Select(Path.GetFileName)!);
        return anchors
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList()!;
    }

    private static bool IsIgnoredDirectory(string name)
    {
        return name is ".git" or ".vs" or ".idea" or "bin" or "obj" or "node_modules" or "dist" or "build";
    }

    private static string FormatStatus(AgentRunStatus status)
    {
        return status switch
        {
            AgentRunStatus.Completed => "完成",
            AgentRunStatus.BudgetExceeded => "已暂停",
            AgentRunStatus.Cancelled => "已停止",
            AgentRunStatus.Failed => "失败",
            _ => "运行中"
        };
    }

    private static string Trim(string value, int maxChars)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars] + "...";
    }
}
