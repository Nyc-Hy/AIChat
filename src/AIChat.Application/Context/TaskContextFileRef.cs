namespace AIChat.Application.Context;

public sealed class TaskContextFileRef
{
    public string Path { get; init; } = "";
    public string TypeTag { get; init; } = "";
    public long SizeBytes { get; init; }
    public double Score { get; init; }
    public string Reason { get; init; } = "";
}
