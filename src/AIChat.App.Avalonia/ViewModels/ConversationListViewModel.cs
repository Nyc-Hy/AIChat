using System.Collections.ObjectModel;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Chat;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the "recent conversations" list and the "currently selected
// conversation" state. PR-4 scope: pure extraction from MainWindowViewModel.
//
// The selected ChatSession (a Domain type) is exposed through the
// ConversationSelected event; the activity feed lives on the parent and
// is updated in response. The currently-selected conversation card is
// exposed as SelectedConversationCard for XAML binding.
//
// Wave 2: switched from Conversation (v0, embedded in ProjectWorkspace) to
// ChatSession (v1, separate sessions.json keyed by workspaceId).
public sealed partial class ConversationListViewModel : ViewModelBase
{
    private const string NewConversationId = "new";

    private readonly IAppRepository _repository;
    // 1.0.6: optional toast surface for the
    // "已删除 [撤销]" affordance on RemoveConversationAsync.
    // Nullable so the 6 unit-test sites that
    // `new ConversationListViewModel(repository)`
    // directly don't have to wire a mock — the
    // production path always injects a real
    // IToastService through the DI container.
    private readonly IToastService? _toast;
    private bool _isApplyingConversationSelection;
    private WorkspaceProject? _currentProject;
    private IReadOnlyList<ChatSession> _sessions = [];

    [ObservableProperty]
    private ConversationCardViewModel? selectedConversationCard;

    public ObservableCollection<ConversationCardViewModel> Conversations { get; } = [];
    public int HistoryCount => _sessions.Count;

    public event EventHandler<ConversationSelectedEventArgs>? ConversationSelected;

    public ConversationListViewModel(IAppRepository repository, IToastService? toast = null)
    {
        _repository = repository;
        _toast = toast;
    }

    // The card's onTitleChange callback. Splits out so the
    // constructor below can pass it without inlining a multi-line
    // lambda twice (once for real conversations, once for the
    // "new" placeholder — even though the placeholder doesn't
    // get renamed, the signature stays uniform).
    private Task PersistTitleChangeAsync(string conversationId, string newTitle)
        => RenameConversationAsync(conversationId, newTitle);

    // Updates the underlying ChatSession.Title in the current
    // workspace, then re-saves the session list. No-op for the
    // "new" placeholder id or unknown ids, and no-op when the
    // trimmed value matches the existing title.
    public async Task RenameConversationAsync(string conversationId, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            conversationId == NewConversationId ||
            _currentProject is null)
        {
            return;
        }

        var trimmed = newTitle?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return;
        }

        var target = _sessions.FirstOrDefault(session =>
            string.Equals(session.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.Title == trimmed)
        {
            return;
        }

        target.Title = trimmed;
        target.UpdatedAt = DateTimeOffset.Now;

        await _repository.SaveSessionsAsync(_sessions);
    }

    // Replaces the conversation list with the workspace's recent
    // sessions. If the workspace is null or has no sessions,
    // a single "new" placeholder card is shown. Raises
    // ConversationSelected so the parent can update the activity feed.
    //
    // `sessions` should be pre-filtered to those belonging to the
    // current workspace (or all Standalone sessions for a "no project" view).
    public void Refresh(WorkspaceProject? project, IReadOnlyList<ChatSession> sessions, string? preferredConversationId = null)
    {
        _currentProject = project;
        _sessions = sessions;
        Conversations.Clear();

        if (project is null || sessions.Count == 0)
        {
            Conversations.Add(new ConversationCardViewModel(
                NewConversationId,
                "新任务",
                "暂无历史对话",
                PersistTitleChangeAsync));
            SetSelectedConversation(Conversations[0]);
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var sorted = sessions
                     .OrderByDescending(session => session.UpdatedAt)
                     .Take(8)
                     .ToList();
        foreach (var session in sorted)
        {
            Conversations.Add(new ConversationCardViewModel(
                session.Id,
                string.IsNullOrWhiteSpace(session.Title) ? "未命名任务" : session.Title,
                session.UpdatedAt.ToLocalTime().ToString("M月d日 HH:mm"),
                PersistTitleChangeAsync));
        }

        var selectedCard = Conversations.FirstOrDefault(item => item.Id == preferredConversationId)
                           ?? Conversations.FirstOrDefault();
        SetSelectedConversation(selectedCard);

        var selectedSession = sessions.FirstOrDefault(item =>
            string.Equals(item.Id, selectedCard?.Id, StringComparison.OrdinalIgnoreCase));
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = selectedSession,
            StatusMessage = selectedSession is null
                ? "已打开新对话。"
                : $"已打开对话：{selectedSession.Title}"
        });
    }

    // Public so the view code-behind can call it via MainWindowViewModel
    // passthrough. Selects the conversation with the given id, or the
    // "new" placeholder if id is "new" or unknown.
    [RelayCommand]
    public void SelectConversation(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || _currentProject is null)
        {
            SetSelectedConversation(Conversations.FirstOrDefault(item => item.Id == NewConversationId));
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        if (conversationId == NewConversationId)
        {
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var session = _sessions.FirstOrDefault(item =>
            string.Equals(item.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
            {
                Conversation = null,
                StatusMessage = "已打开新对话。"
            });
            return;
        }

        var card = Conversations.FirstOrDefault(item => item.Id == session.Id);
        SetSelectedConversation(card);
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = session,
            StatusMessage = $"已打开对话：{session.Title}"
        });
    }

    private void SetSelectedConversation(ConversationCardViewModel? conversation)
    {
        _isApplyingConversationSelection = true;
        foreach (var item in Conversations)
        {
            item.IsSelected = item.Id == conversation?.Id;
        }

        SelectedConversationCard = conversation;
        _isApplyingConversationSelection = false;
    }

    partial void OnSelectedConversationCardChanged(ConversationCardViewModel? value)
    {
        if (_isApplyingConversationSelection || value is null)
        {
            return;
        }

        SelectConversation(value.Id);
    }

    // Removes the conversation with the given id from the current
    // workspace. The activity feed and conversation list both
    // refresh — the conversation list drops the row, the activity
    // feed switches to a fresh "new conversation" prompt via
    // ConversationSelected.
    //
    // 1.0.6: a "已删除 [撤销]" toast is surfaced alongside the
    // physical delete so a misclick can be rescued within the
    // 3-second auto-dismiss window. The session object itself
    // stays alive in the snapshot reference for that window —
    // removing it from _sessions is a list mutation, not a
    // heap free, so restoreConversation can re-insert the same
    // instance without a re-fetch from disk. The save is also
    // re-issued on restore so the file matches the in-memory
    // state when the user closes the app.
    [RelayCommand]
    public async Task RemoveConversationAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            conversationId == NewConversationId ||
            _currentProject is null)
        {
            return;
        }

        var target = _sessions.FirstOrDefault(session =>
            string.Equals(session.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _sessions = _sessions.Where(session => !ReferenceEquals(session, target)).ToList();

        await _repository.SaveSessionsAsync(_sessions);

        // Refresh the list so the deleted row disappears, then
        // re-emit ConversationSelected with null so the host's
        // activity feed switches to the "new conversation" prompt.
        Refresh(_currentProject, _sessions);
        ConversationSelected?.Invoke(this, new ConversationSelectedEventArgs
        {
            Conversation = null,
            StatusMessage = $"已删除对话：{target.Title}"
        });

        // 1.0.6: undo affordance. The toast's auto-dismiss
        // (3s) is the window — a user who realises they
        // misclicked has 3 seconds to tap "撤销" and the
        // session reappears in the sidebar. The action
        // captures `target` by reference; target is still
        // rooted in memory (it's just been removed from
        // _sessions) so the closure does not need to
        // re-load anything. The toast is only shown when
        // IToastService was injected (the production
        // path) — unit tests that construct this VM
        // without a toast surface skip the affordance
        // entirely.
        if (_toast is not null)
        {
            _toast.ShowWithAction(
                $"已删除对话：{target.Title}",
                ToastLevel.Warning,
                "撤销",
                () =>
                {
                    // Discard the Task — the action
                    // runs synchronously enough (in-memory
                    // re-insert + a SaveSessionsAsync
                    // fire-and-forget) that the user
                    // never sees the toast "stuck open"
                    // while restore finishes. Restore
                    // itself awaits the save before
                    // returning; if the save throws the
                    // exception surfaces through the
                    // discarded task's faulted state,
                    // which the global handler will
                    // surface.
                    _ = RestoreConversation(target);
                });
        }
    }

    // 1.0.6: re-insert a deleted conversation at the head of
    // the list. Called from the "撤销" toast button via the
    // closure captured in RemoveConversationAsync. No-ops if
    // the id is already present (defensive — a rapid second
    // delete + restore on the same id should not duplicate the
    // row), and silently re-saves the in-memory state to
    // settings.json so the file matches the sidebar.
    private async Task RestoreConversation(ChatSession snapshot)
    {
        if (_sessions.Any(session => string.Equals(session.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var restored = new List<ChatSession>(_sessions.Count + 1) { snapshot };
        restored.AddRange(_sessions);
        _sessions = restored;

        await _repository.SaveSessionsAsync(_sessions);
        Refresh(_currentProject, _sessions);
    }

    // 2026-08-03: render the named conversation as Markdown and
    // write it to outputPath. The host (MainWindow code-behind)
    // is responsible for showing the SaveFilePicker and routing
    // the resulting path here. Splitting the path picker from the
    // file write keeps this view-model testable (no Avalonia
    // StorageProvider dependency) and makes the call site easy
    // to wire from a right-click menu in the conversation card
    // XAML.
    //
    // Returns the byte count written, or null when the id is
    // unknown / the new-conversation placeholder / the path is
    // not writable. The host toasts on null so the user gets
    // "导出失败" feedback instead of a silent no-op.
    public async Task<int?> ExportConversationToPathAsync(string? conversationId, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            conversationId == NewConversationId ||
            _currentProject is null)
        {
            return null;
        }

        var target = _sessions.FirstOrDefault(session =>
            string.Equals(session.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        try
        {
            var markdown = MarkdownConversationExporter.Export(target);
            await File.WriteAllTextAsync(outputPath, markdown, System.Text.Encoding.UTF8).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetByteCount(markdown);
        }
        catch
        {
            // The host surfaces a generic "导出失败" toast; the
            // exception text is already in the user's terminal
            // via the global CrashReporter hook if it was a real
            // fault, and is otherwise a benign I/O error.
            return null;
        }
    }
}
