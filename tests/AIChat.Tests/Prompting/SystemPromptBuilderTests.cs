using AIChat.Abstractions.Configuration;
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
}
