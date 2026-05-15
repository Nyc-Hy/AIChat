using System.IO;
using AIChat.Application.Projects;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record ProjectPreparationResult(bool CanStart, string Summary);

    private async Task<ProjectPreparationResult> PrepareProjectForAgentRunAsync()
    {
        if (SelectedProject is null)
        {
            return new ProjectPreparationResult(false, "未选择项目");
        }

        if (string.IsNullOrWhiteSpace(SelectedProject.Path))
        {
            PromptForProjectPath();
        }

        if (string.IsNullOrWhiteSpace(SelectedProject.Path))
        {
            return new ProjectPreparationResult(false, "未设置项目路径");
        }

        if (!Directory.Exists(SelectedProject.Path))
        {
            StatusText = "项目路径不存在，请先重选项目路径";
            return new ProjectPreparationResult(false, "项目路径不存在");
        }

        var project = SelectedProject.Project;
        var initializer = new ProjectInitializer();
        var prepared = new List<string> { "路径可用" };
        var changed = false;

        var agentsPath = Path.Combine(project.Path, "AGENTS.md");
        if (!File.Exists(agentsPath))
        {
            await initializer.InitializeProjectAsync(project.Path);
            changed = File.Exists(agentsPath);
            prepared.Add(changed ? "已生成 AGENTS.md" : "AGENTS.md 缺失");
        }
        else
        {
            prepared.Add("AGENTS.md 已就绪");
        }

        if (project.VerificationCommands.Count == 0)
        {
            var suggestions = initializer.SuggestVerificationCommands(project.Path);
            if (suggestions.Count > 0)
            {
                project.VerificationCommands = suggestions.ToList();
                LoadProjectVerificationCommands();
                changed = true;
                prepared.Add($"已推断 {suggestions.Count} 个验证命令");
            }
            else
            {
                prepared.Add("无可推断验证命令");
            }
        }
        else
        {
            prepared.Add($"{project.VerificationCommands.Count} 个验证命令");
        }

        if (changed)
        {
            project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
        }

        RaiseProjectLoadSnapshotProperties();
        return new ProjectPreparationResult(true, string.Join(" · ", prepared));
    }
}
