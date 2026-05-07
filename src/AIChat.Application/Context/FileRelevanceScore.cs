namespace AIChat.Application.Context;

public sealed record FileRelevanceScore(
    string Path,
    string TypeTag,
    long SizeBytes,
    double Score,
    string Reason);
