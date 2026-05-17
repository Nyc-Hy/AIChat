using AIChat.Application.Agents.Benchmark;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RunSelectedBenchmarkAsync()
    {
        if (SelectedProject is null || IsSending)
        {
            return;
        }

        var task = AgentBenchmarkSuite.DefaultTasks.FirstOrDefault(item =>
                       string.Equals(item.Id, SelectedBenchmarkTaskId, StringComparison.OrdinalIgnoreCase)) ??
                   AgentBenchmarkSuite.DefaultTasks[0];

        if (SelectedConversation is null)
        {
            SelectConversation(SelectedProject.CreateConversation());
        }

        DraftMessage = AgentBenchmarkPromptBuilder.Build(task, SelectedProject.Name);
        StatusText = $"准备运行 Benchmark：{task.Name}";
        await SendAsync();
    }
}
