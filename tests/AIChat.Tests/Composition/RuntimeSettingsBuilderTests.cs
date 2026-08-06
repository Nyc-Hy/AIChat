using AIChat.Abstractions.Configuration;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Tools;

namespace AIChat.Tests.Composition;

// PR-7 tests: RuntimeSettingsBuilder builds the AppSettings handed to the
// agent harness for each execution mode. ReadOnly + Gui are verified
// independently to make sure the round budgets and permission policies
// stay correct. (Plain was removed in e033937 — the agent runner only
// ever picks between ReadOnly and Gui.)
public class RuntimeSettingsBuilderTests
{
    [Fact]
    public void ReadOnly_EnablesOnlyReadOnlyAndUpdatePlanTools()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var source = new AppSettings { AgentMaxToolRounds = 50 };

        var runtime = RuntimeSettingsBuilder.ReadOnly(source, registry);

        // Read-only tools: list_files, read_input_artifact, read_file,
        // search_text, git_status, git_diff, update_plan.
        Assert.NotEmpty(runtime.EnabledToolIds);
        Assert.Contains("read_file", runtime.EnabledToolIds);
        Assert.Contains("list_files", runtime.EnabledToolIds);
        Assert.Contains("update_plan", runtime.EnabledToolIds);
        Assert.DoesNotContain("write_file", runtime.EnabledToolIds);
        Assert.DoesNotContain("run_shell", runtime.EnabledToolIds);
        Assert.DoesNotContain("edit_file", runtime.EnabledToolIds);

        Assert.Equal(ToolPermissionMode.AutoReadOnly, runtime.ToolPermissionModes["read_file"]);
        Assert.Equal(ToolPermissionMode.Disabled, runtime.ToolPermissionModes["write_file"]);
        Assert.Equal(ToolPermissionMode.Disabled, runtime.ToolPermissionModes["run_shell"]);

        // Read-only caps the round budget at 8.
        Assert.Equal(8, runtime.AgentMaxToolRounds);
    }

    [Fact]
    public void Gui_EnablesAllToolsAndRequiresApprovalForMutating()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var source = new AppSettings
        {
            AgentMaxToolRounds = 30,
            AutoVerifyAgentRuns = true,
            MaxAutoFixRounds = 2
        };

        var runtime = RuntimeSettingsBuilder.Gui(source, registry);

        Assert.Equal(registry.All.Count, runtime.EnabledToolIds.Count);
        Assert.Contains("read_file", runtime.EnabledToolIds);
        Assert.Contains("write_file", runtime.EnabledToolIds);
        Assert.Contains("run_shell", runtime.EnabledToolIds);
        Assert.Contains("update_plan", runtime.EnabledToolIds);

        Assert.Equal(ToolPermissionMode.AutoReadOnly, runtime.ToolPermissionModes["read_file"]);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, runtime.ToolPermissionModes["update_plan"]);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, runtime.ToolPermissionModes["write_file"]);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, runtime.ToolPermissionModes["run_shell"]);

        Assert.Equal(12, runtime.AgentMaxToolRounds);
        Assert.True(runtime.AutoVerifyAgentRuns);
        Assert.Equal(2, runtime.MaxAutoFixRounds);
    }

    [Fact]
    public void ReadOnly_CapsLargeRoundBudget()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var source = new AppSettings { AgentMaxToolRounds = 100 };

        var runtime = RuntimeSettingsBuilder.ReadOnly(source, registry);

        Assert.Equal(8, runtime.AgentMaxToolRounds);
    }

    [Fact]
    public void Gui_CapsLargeRoundBudget()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var source = new AppSettings { AgentMaxToolRounds = 100 };

        var runtime = RuntimeSettingsBuilder.Gui(source, registry);

        Assert.Equal(12, runtime.AgentMaxToolRounds);
    }
}
