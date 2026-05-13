using AIChat.Domain.Memory;

namespace AIChat.App.ViewModels;

public sealed class ProjectMemoryViewModel : ObservableObject
{
    public ProjectMemoryViewModel(MemoryEntry memory, bool isPending)
    {
        Memory = memory;
        IsPending = isPending;
    }

    public MemoryEntry Memory { get; }
    public string Id => Memory.Id;
    public bool IsPending { get; }
    public MemoryCategory Category => Memory.Category;
    public string CategoryText => Memory.Category switch
    {
        MemoryCategory.Project => "项目",
        MemoryCategory.Task => "任务",
        MemoryCategory.Tool => "工具",
        MemoryCategory.User => "用户",
        _ => Memory.Category.ToString()
    };
    public string StateText => IsPending ? "待确认" : "已保存";
    public string Content => Memory.Content;
    public string Source => string.IsNullOrWhiteSpace(Memory.Source) ? "unknown" : Memory.Source;
    public string CreatedAtText => Memory.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string MetadataText => Memory.Metadata.Count == 0
        ? ""
        : string.Join(" · ", Memory.Metadata
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Take(4)
            .Select(pair => $"{pair.Key}={pair.Value}"));
}
