using System.Collections.ObjectModel;
using AIChat.Application.Scheduled;
using AIChat.Domain.Scheduled;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Wave 9 (parity plan §7 Wave 9): the modal VM for the
// "已安排" (Scheduled) panel. First slice: read-only list
// + per-row pause / resume / run-now / edit / delete, plus
// an "添加任务" form. The actual cron / scheduler engine
// that fires tasks on a timer lands in a follow-up slice;
// for now "记录运行" routes through the registry (the
// XAML label is the honest placeholder — see
// RunNowAsync for the "why no real execution" comment).
// runner so the user can verify the prompt + project pair
// works end-to-end.
public sealed partial class ScheduledViewModel : ViewModelBase
{
    private readonly IScheduledTaskRegistry _registry;

    public ScheduledViewModel(IScheduledTaskRegistry registry)
    {
        _registry = registry;
        _registry.Changed += OnRegistryChanged;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        ReloadAsync().FireAndForget();
    }

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<ScheduledTaskRowViewModel> Tasks { get; } = [];

    public IAsyncRelayCommand ReloadCommand { get; } = null!;

    [RelayCommand]
    private async Task AddAsync()
    {
        // Default to the first project's id (or empty
        // string for "no project yet"). The user fills the
        // form in a follow-up edit; the first slice
        // accepts the defaults so the row lands in the
        // list and the user can verify persistence.
        var task = new ScheduledTask
        {
            Name = "新任务",
            Prompt = "请检查项目最新提交并总结。",
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:00",
        };
        await _registry.AddAsync(task);
    }

    [RelayCommand]
    private async Task PauseAsync(ScheduledTaskRowViewModel? row)
    {
        if (row is null) return;
        await _registry.SetPausedAsync(row.Id, true);
    }

    [RelayCommand]
    private async Task ResumeAsync(ScheduledTaskRowViewModel? row)
    {
        if (row is null) return;
        await _registry.SetPausedAsync(row.Id, false);
    }

    [RelayCommand]
    private async Task RunNowAsync(ScheduledTaskRowViewModel? row)
    {
        if (row is null) return;

        // First slice: record a "Running" entry in the
        // history list so the user sees the action took
        // effect. The actual agent-runner invocation
        // (which would route the prompt through
        // AgentHost.SendTaskAsync) lands in a follow-up
        // slice — the runner needs to know about scheduled
        // runs specifically to apply the
        // "approval-on-no-human-interaction" rule from
        // plan §7 Wave 9. Until then, the user verifies
        // the schedule via the registry data, not via
        // real prompt execution.
        //
        // The command is named RunNow because that's the
        // closest honest match to the Codex-side action
        // (and the field name is baked into the XAML
        // tooltip). The visible button label + tooltip on
        // the XAML side use "记录运行" / "Wave 9 follow-up"
        // to make the placeholder behavior obvious.
        await _registry.RecordRunAsync(new ScheduledTaskRun
        {
            ScheduledTaskId = row.Id,
            Status = ScheduledRunStatus.Running,
            Output = "已加入队列 (Wave 9 后续接入真实执行)",
        });
    }

    [RelayCommand]
    private async Task RemoveAsync(ScheduledTaskRowViewModel? row)
    {
        if (row is null) return;
        await _registry.RemoveAsync(row.Id);
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _registry.ReloadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"刷新失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        // The registry fires Changed on whatever thread
        // ran the last mutation. Marshal back to UI thread
        // before touching the ObservableCollection.
        Dispatcher.UIThread.Post(() =>
        {
            Tasks.Clear();
            foreach (var task in _registry.Tasks.OrderBy(t => t.CreatedAt))
            {
                Tasks.Add(new ScheduledTaskRowViewModel(task));
            }
            IsLoading = false;
        });
    }
}

// One row in the Scheduled list. Mirrors the data
// fields the user actually reads (Name / Project / Cadence
// / Status / Last Run) and flattens the cadence + run
// state into a single StatusLabel so the list stays
// scannable. Commands live on the parent VM (passed
// through the row) — keeping them here would force the
// XAML to instantiate a per-row command factory.
public sealed class ScheduledTaskRowViewModel
{
    public string Id { get; }
    public string Name { get; }
    public string ProjectId { get; }
    public string Prompt { get; }
    public string CadenceLabel { get; }
    public string StatusLabel { get; }
    public string LastRunLabel { get; }
    public bool IsPaused { get; }

    public ScheduledTaskRowViewModel(ScheduledTask task)
    {
        Id = task.Id;
        Name = string.IsNullOrWhiteSpace(task.Name) ? "（未命名）" : task.Name;
        ProjectId = task.ProjectId;
        Prompt = task.Prompt;
        IsPaused = task.IsPaused;
        CadenceLabel = FormatCadence(task);
        StatusLabel = task.IsPaused ? "已暂停" : "已启用";
        LastRunLabel = task.LastRunAt is null
            ? "尚未运行"
            : FormatRelative(task.LastRunAt.Value);
    }

    private static string FormatCadence(ScheduledTask task) => task.Cadence switch
    {
        ScheduledCadence.Manual => "手动",
        ScheduledCadence.Once => "单次",
        ScheduledCadence.Daily => $"每日 {task.CadenceTime}",
        ScheduledCadence.Weekly => $"每周 {task.CadenceTime}",
        _ => "未知",
    };

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalSeconds < 60) return "刚刚";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} 分钟前";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} 小时前";
        return when.LocalDateTime.ToString("MM-dd HH:mm");
    }
}
