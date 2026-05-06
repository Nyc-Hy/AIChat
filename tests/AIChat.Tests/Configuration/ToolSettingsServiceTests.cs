using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;

namespace AIChat.Tests.Configuration;

public sealed class ToolSettingsServiceTests
{
    [Fact]
    public void Normalize_FiltersUnknownToolsAndFillsDefaultPermissionModes()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var settings = new AppSettings
        {
            EnabledToolIds = ["read_file", "unknown", "read_file"],
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["read_file"] = ToolPermissionMode.AutoReadOnly,
                ["unknown"] = ToolPermissionMode.AllowForSession
            }
        };

        ToolSettingsService.Normalize(settings, registry);

        Assert.Equal(["read_file"], settings.EnabledToolIds);
        Assert.DoesNotContain("unknown", settings.ToolPermissionModes.Keys);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, settings.ToolPermissionModes["write_file"]);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, settings.ToolPermissionModes["read_file"]);
    }

    [Fact]
    public void Normalize_EmptyEnabledToolsEnablesAllKnownTools()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var settings = new AppSettings
        {
            EnabledToolIds = []
        };

        ToolSettingsService.Normalize(settings, registry);

        Assert.Equal(registry.All.Count, settings.EnabledToolIds.Count);
        Assert.Contains("read_file", settings.EnabledToolIds);
        Assert.Contains("run_shell", settings.EnabledToolIds);
    }

    [Fact]
    public void Normalize_LegacyGitStatusAndDiffAddsRestoreAndCommit()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var settings = new AppSettings
        {
            EnabledToolIds = ["git_status", "git_diff"]
        };

        ToolSettingsService.Normalize(settings, registry);

        Assert.Contains("git_restore_file", settings.EnabledToolIds);
        Assert.Contains("git_commit", settings.EnabledToolIds);
    }

    [Fact]
    public void SyncToolOptions_DisabledModeRemovesToolFromEnabledListButKeepsMode()
    {
        var settings = new AppSettings();

        ToolSettingsService.SyncToolOptions(
            settings,
            [
                ("read_file", true, nameof(ToolPermissionMode.AutoReadOnly)),
                ("write_file", true, nameof(ToolPermissionMode.Disabled)),
                ("run_shell", false, nameof(ToolPermissionMode.AllowForSession)),
                ("bad_mode", true, "not-a-mode")
            ]);

        Assert.Contains("read_file", settings.EnabledToolIds);
        Assert.Contains("bad_mode", settings.EnabledToolIds);
        Assert.DoesNotContain("write_file", settings.EnabledToolIds);
        Assert.DoesNotContain("run_shell", settings.EnabledToolIds);
        Assert.Equal(ToolPermissionMode.Disabled, settings.ToolPermissionModes["write_file"]);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, settings.ToolPermissionModes["bad_mode"]);
    }

    [Fact]
    public void MergePermissionModes_ValidProjectOverridesReplaceGlobalModes()
    {
        var global = new Dictionary<string, ToolPermissionMode>
        {
            ["read_file"] = ToolPermissionMode.AutoReadOnly,
            ["write_file"] = ToolPermissionMode.ConfirmEachTime
        };

        var merged = ToolSettingsService.MergePermissionModes(
            global,
            new Dictionary<string, string>
            {
                ["write_file"] = nameof(ToolPermissionMode.AllowForSession),
                ["run_shell"] = "not-a-mode"
            });

        Assert.Equal(ToolPermissionMode.AutoReadOnly, merged["read_file"]);
        Assert.Equal(ToolPermissionMode.AllowForSession, merged["write_file"]);
        Assert.DoesNotContain("run_shell", merged.Keys);
    }

    [Fact]
    public void CreateProjectOverrides_DropsBlankToolIdsAndKeepsLastDuplicateIgnoringCase()
    {
        var overrides = ToolSettingsService.CreateProjectOverrides(
            [
                ("read_file", nameof(ToolPermissionMode.AutoReadOnly)),
                ("", nameof(ToolPermissionMode.Disabled)),
                ("READ_FILE", nameof(ToolPermissionMode.AllowForSession))
            ]);

        Assert.Single(overrides);
        Assert.Equal(ToolPermissionMode.AllowForSession.ToString(), overrides["read_file"]);
    }

    [Fact]
    public void CreateToolOptions_ProjectsRegistryToolsWithSettingsState()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var settings = new AppSettings
        {
            EnabledToolIds = ["read_file"],
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["read_file"] = ToolPermissionMode.AutoReadOnly,
                ["write_file"] = ToolPermissionMode.AllowForSession
            }
        };

        var options = ToolSettingsService.CreateToolOptions(settings, registry);

        var read = Assert.Single(options, option => option.Id == "read_file");
        Assert.True(read.IsEnabled);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, read.PermissionMode);

        var write = Assert.Single(options, option => option.Id == "write_file");
        Assert.False(write.IsEnabled);
        Assert.Equal(ToolPermissionMode.AllowForSession, write.PermissionMode);
    }
}
