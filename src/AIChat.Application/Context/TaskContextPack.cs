namespace AIChat.Application.Context;

public sealed class TaskContextPack
{
    public string Summary { get; init; } = "";
    public IReadOnlyList<TaskContextFileRef> IncludedFiles { get; init; } = [];
    public IReadOnlyList<string> IncludedSnippets { get; init; } = [];
    public IReadOnlyList<string> ArtifactRefs { get; init; } = [];
    public IReadOnlyList<TaskContextFileRef> OmittedButRelevantRefs { get; init; } = [];
    public int EstimatedTokens { get; init; }

    public IReadOnlyList<string> ToPromptRefs()
    {
        var refs = new List<string>();
        if (!string.IsNullOrWhiteSpace(Summary))
        {
            refs.Add(Summary);
        }

        refs.AddRange(IncludedFiles.Select(file => $"{file.Path} ({file.Reason}, score {file.Score:0.##})"));
        refs.AddRange(IncludedSnippets);
        refs.AddRange(ArtifactRefs.Select(artifact => $"artifact: {artifact}"));
        if (OmittedButRelevantRefs.Count > 0)
        {
            refs.Add("omitted relevant: " + string.Join(", ", OmittedButRelevantRefs.Select(file => file.Path)));
        }

        return refs;
    }
}
