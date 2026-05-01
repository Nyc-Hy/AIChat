using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentFileChangeViewModel : ObservableObject
{
    private readonly AgentFileChange _fileChange;

    public AgentFileChangeViewModel(AgentFileChange fileChange)
    {
        _fileChange = fileChange;
    }

    public string Id => _fileChange.Id;
    public string Path => _fileChange.Path;
    public string ToolName => _fileChange.ToolName;
    public string DiffText => _fileChange.DiffText;
    public IReadOnlyList<DiffLineViewModel> DiffLines => DiffLineViewModel.FromDiff(_fileChange.DiffText);
    public string SizeText => $"{_fileChange.OldChars} -> {_fileChange.NewChars} chars";
    public bool HasDiff => !string.IsNullOrWhiteSpace(_fileChange.DiffText);
    public string PostChangeHash => _fileChange.PostChangeHash;
    public string ContentSnapshot => _fileChange.ContentSnapshot;
}
