using AIChat.Application.Agents.Benchmark;

namespace AIChat.App.ViewModels;

public sealed class AgentBenchmarkTaskOptionViewModel
{
    private readonly AgentBenchmarkTask _task;

    public AgentBenchmarkTaskOptionViewModel(AgentBenchmarkTask task)
    {
        _task = task;
    }

    public AgentBenchmarkTask Task => _task;
    public string Id => _task.Id;
    public string Name => _task.Name;
    public string Description => $"{_task.Category} · 工具 <= {_task.MaxToolCalls} · Context <= {_task.MaxEstimatedPromptTokens}";
}
