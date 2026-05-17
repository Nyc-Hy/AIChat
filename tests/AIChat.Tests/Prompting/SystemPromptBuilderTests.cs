using AIChat.Abstractions.Configuration;
using AIChat.Application.Plugins;
using AIChat.Application.Prompting;

namespace AIChat.Tests.Prompting;

public sealed class SystemPromptBuilderTests
{
    [Fact]
    public void Build_IncludesProjectAndToolPermissions()
    {
        var prompt = new SystemPromptBuilder().Build(new SystemPromptContext
        {
            ProjectName = "Demo",
            ProjectPath = @"D:\Code\Demo",
            EnabledToolIds = ["read_file", "write_file"],
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase)
            {
                ["read_file"] = ToolPermissionMode.AutoReadOnly,
                ["write_file"] = ToolPermissionMode.ConfirmEachTime
            }
        });

        Assert.Contains("名称：Demo", prompt);
        Assert.Contains(@"路径：D:\Code\Demo", prompt);
        Assert.Contains("read_file：只读工具可自动执行", prompt);
        Assert.Contains("write_file：每次执行前需要确认", prompt);
        Assert.Contains("不能声称", new SystemPromptBuilder().Build(new SystemPromptContext()));
    }

    [Fact]
    public void Build_IncludesToolPreferencePolicy()
    {
        var prompt = new SystemPromptBuilder().Build(new SystemPromptContext
        {
            EnabledToolIds =
            [
                "apply_patch",
                "git_status",
                "git_diff",
                "git_restore_file",
                "git_commit",
                "run_build",
                "run_test",
                "run_shell"
            ]
        });

        Assert.Contains("优先用 apply_patch", prompt);
        Assert.Contains("git_status 和 git_diff", prompt);
        Assert.Contains("git_restore_file", prompt);
        Assert.Contains("git_commit", prompt);
        Assert.Contains("不要用 run_shell 执行 git add/commit", prompt);
        Assert.Contains("run_build 或 run_test", prompt);
        Assert.Contains("run_shell 只作为最后手段", prompt);
    }

    [Fact]
    public void Build_IncludesPlanGuidanceWhenUpdatePlanEnabled()
    {
        var prompt = new SystemPromptBuilder().Build(new SystemPromptContext
        {
            EnabledToolIds = ["update_plan", "read_file", "write_file"]
        });

        Assert.Contains("update_plan", prompt);
        Assert.Contains("多步骤任务", prompt);
        Assert.Contains("先调用 update_plan 创建计划", prompt);
    }

    [Fact]
    public void Build_IncludesDeepSeekInstructions_WhenProviderIsDeepSeek()
    {
        var prompt = new SystemPromptBuilder().Build(new SystemPromptContext
        {
            ProviderId = "deepseek",
            EnabledToolIds = ["read_file"]
        });

        Assert.Contains("DeepSeek 模型提示", prompt);
        Assert.Contains("思考模式", prompt);
        Assert.Contains("推理强度", prompt);
    }

    [Fact]
    public void Build_ExcludesDeepSeekInstructions_WhenProviderIsMiMo()
    {
        var prompt = new SystemPromptBuilder().Build(new SystemPromptContext
        {
            ProviderId = "tokenplan-mimo",
            EnabledToolIds = ["read_file"]
        });

        Assert.DoesNotContain("DeepSeek 模型提示", prompt);
    }

    [Fact]
    public void Build_IncludesPluginSkills()
    {
        var prompt = new SystemPromptBuilder(
            [
                new PluginSkill(
                    "dotnet_tools",
                    "dotnet_tools_helper",
                    "Dotnet Helper",
                    "Use for .NET work.",
                    "Prefer targeted dotnet test commands.",
                    @"C:\plugins\dotnet\SKILL.md")
            ]).Build(new SystemPromptContext
            {
                EnabledToolIds = ["read_file"]
            });

        Assert.Contains("已启用插件 Skill", prompt);
        Assert.Contains("Dotnet Helper", prompt);
        Assert.Contains("Prefer targeted dotnet test commands.", prompt);
    }
}
