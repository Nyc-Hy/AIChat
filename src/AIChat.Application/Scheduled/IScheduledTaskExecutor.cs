using AIChat.Domain.Scheduled;

namespace AIChat.Application.Scheduled;

// Abstraction the runner uses to fire a due task. Lives in
// AIChat.Application so the runner (pure logic, no UI deps)
// can be unit-tested with a fake executor. The real
// implementation lives in AIChat.App.Avalonia (the desktop
// host) and routes through AgentHost.SendTaskAsync — the
// runner doesn't know or care about Avalonia / the agent
// loop, only that "execute this prompt, record the result".
//
// Why an interface instead of injecting AgentHostViewModel
// directly: AgentHostViewModel lives in AIChat.App.Avalonia
// and pulls in Avalonia + MVVM toolkit + Dispatcher. The
// Application layer is meant to be Avalonia-free; the
// executor boundary is the seam.
public interface IScheduledTaskExecutor
{
    // Run the task now. The returned ScheduledTaskRun is
    // the result the runner should append to the history
    // (the executor handles StartedAt / CompletedAt /
    // Status / Output / ErrorMessage). The runner calls
    // RecordRunAsync itself with whatever the executor
    // returns so the LastRunAt flip lives in the registry.
    Task<ScheduledTaskRun> ExecuteAsync(
        ScheduledTask task,
        CancellationToken cancellationToken = default);
}
