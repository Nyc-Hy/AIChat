namespace AIChat.Application.Agents;

public sealed class AgentRunQueue
{
    private readonly object _lock = new();
    private string? _currentRunId;

    public bool IsRunning
    {
        get { lock (_lock) { return _currentRunId is not null; } }
    }

    public string? CurrentRunId
    {
        get { lock (_lock) { return _currentRunId; } }
    }

    public bool TryStart(string runId)
    {
        lock (_lock)
        {
            if (_currentRunId is not null)
            {
                return false;
            }

            _currentRunId = runId;
            return true;
        }
    }

    public void Complete(string runId)
    {
        lock (_lock)
        {
            if (string.Equals(_currentRunId, runId, StringComparison.Ordinal))
            {
                _currentRunId = null;
            }
        }
    }
}
