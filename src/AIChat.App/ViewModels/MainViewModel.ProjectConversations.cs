using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using AIChat.App.Controls;
using AIChat.App.Services;
using AIChat.Application.Artifacts;
using AIChat.Application.Projects;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public ProjectViewModel? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(CurrentProjectName));
                RaiseProjectLoadSnapshotProperties();
                NewChatCommand.RaiseCanExecuteChanged();
                RefreshWorkspaceChangesCommand.RaiseCanExecuteChanged();
                AttachInputArtifactCommand.RaiseCanExecuteChanged();
                AddProjectVerificationCommandCommand.RaiseCanExecuteChanged();
                InferProjectVerificationCommandsCommand.RaiseCanExecuteChanged();
                RebuildCurrentInputArtifacts();
                LoadProjectToolPermissionOverrides();
                LoadProjectVerificationCommands();
            }
        }
    }

    public ConversationViewModel? SelectedConversation
    {
        get => _selectedConversation;
        private set
        {
            if (SetProperty(ref _selectedConversation, value))
            {
                OnPropertyChanged(nameof(CurrentConversationTitle));
                OnPropertyChanged(nameof(AgentRunHistoryTitle));
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(HasMessages));
                OnPropertyChanged(nameof(HasHiddenMessages));
                OnPropertyChanged(nameof(LoadEarlierMessagesText));
                LoadEarlierMessagesCommand.RaiseCanExecuteChanged();
                OpenAgentRunHistoryCommand.RaiseCanExecuteChanged();
                AttachInputArtifactCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CurrentInputArtifactSummary));
                RebuildCurrentInputArtifacts();
                UpdateContextUsage();
                RebuildAgentRunHistoryIfOpen();
            }
        }
    }

    public ObservableCollection<ChatMessageViewModel>? Messages => SelectedConversation?.Messages;
    public bool HasMessages => Messages?.Count > 0;
    public bool HasHiddenMessages => SelectedConversation?.HasHiddenMessages == true;
    public string LoadEarlierMessagesText => SelectedConversation?.LoadEarlierMessagesText ?? "";
    public string CurrentProjectName => SelectedProject?.Name ?? "未选择项目";
    public string WindowTitle
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version is not null ? $" v{version.Major}.{version.Minor}.{version.Build}" : "";
            var project = SelectedProject?.Name;
            return string.IsNullOrEmpty(project) ? $"AIChat{versionStr}" : $"AIChat{versionStr} — {project}";
        }
    }
    public string CurrentConversationTitle => SelectedConversation?.Title ?? "新对话";
    public string CurrentProjectHealthText => _projectLoadSnapshot.HealthText;
    public string CurrentProjectProfileText => _projectLoadSnapshot.ProfileText;
    public string CurrentProjectActivityText => _projectLoadSnapshot.ActivityText;
    public string CurrentProjectRecommendationText => _projectLoadSnapshot.RecommendationText;
    public bool HasCurrentInputArtifacts => CurrentInputArtifacts.Count > 0;
    public string CurrentInputArtifactSummary
    {
        get
        {
            var summary = BuildCurrentInputArtifactDeliverySummary(ActiveModelSupportsVision);
            return summary.TotalCount == 0 ? "" : summary.SummaryText;
        }
    }

    public string ConversationSearchText
    {
        get => _conversationSearchText;
        set
        {
            if (SetProperty(ref _conversationSearchText, value))
            {
                ApplyConversationFilters();
            }
        }
    }

    public bool IsRemoveProjectConfirmationOpen
    {
        get => _isRemoveProjectConfirmationOpen;
        private set => SetProperty(ref _isRemoveProjectConfirmationOpen, value);
    }

    public bool IsInputArtifactPreviewOpen
    {
        get => _isInputArtifactPreviewOpen;
        private set => SetProperty(ref _isInputArtifactPreviewOpen, value);
    }

    public InputArtifactViewModel? SelectedInputArtifactPreview
    {
        get => _selectedInputArtifactPreview;
        private set => SetProperty(ref _selectedInputArtifactPreview, value);
    }

    public ProjectViewModel? ProjectPendingRemoval
    {
        get => _projectPendingRemoval;
        private set
        {
            if (SetProperty(ref _projectPendingRemoval, value))
            {
                OnPropertyChanged(nameof(ProjectRemovalName));
                OnPropertyChanged(nameof(ProjectRemovalPathText));
                ConfirmRemoveProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProjectRemovalName => ProjectPendingRemoval?.Name ?? "";

    public string ProjectRemovalPathText => string.IsNullOrWhiteSpace(ProjectPendingRemoval?.Path)
        ? "未设置项目路径"
        : ProjectPendingRemoval.Path;

    private void SelectProject(ProjectViewModel? project)
    {
        if (project is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedProject, project))
        {
            return;
        }

        if (SelectedProject is not null)
        {
            SelectedProject.IsSelected = false;
        }

        project.IsSelected = true;
        SelectedProject = project;
        Settings.LastActiveProjectId = project.Project.Id;
        var conversation = project.Conversations.FirstOrDefault();
        if (conversation is not null)
        {
            SelectConversation(conversation);
        }
        else
        {
            SelectedConversation = null;
        }

        _ = RefreshWorkspaceChangesAsync();
    }

    private void PromptForProjectPath()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = $"请选择 \"{SelectedProject!.Name}\" 的项目文件夹",
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.SelectedPath))
        {
            SelectedProject.Project.Path = dialog.SelectedPath;
            EnsureDefaultVerificationCommands(SelectedProject.Project);
            _ = _repository.SaveProjectsAsync(Projects.Select(p => p.Project).ToList());
            OnPropertyChanged(nameof(SelectedProject));
            RaiseProjectLoadSnapshotProperties();
            LoadProjectVerificationCommands();
            InferProjectVerificationCommandsCommand.RaiseCanExecuteChanged();
            StatusText = $"项目路径已设置为：{dialog.SelectedPath}";
        }
        else
        {
            StatusText = "未设置项目路径，工具将以应用目录为根路径运行。请稍后通过「添加项目」配置正确路径。";
        }
    }

    private async Task AddProjectAsync()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = "选择项目文件夹",
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folderPath = dialog.SelectedPath;
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        // Check for duplicates
        if (Projects.Any(project => string.Equals(project.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            // Already added — just select it
            SelectProject(Projects.First(project => string.Equals(project.Path, folderPath, StringComparison.OrdinalIgnoreCase)));
            return;
        }

        var projectName = Path.GetFileName(folderPath);
        var workspace = new ProjectWorkspace
        {
            Name = projectName,
            Path = folderPath,
            UpdatedAt = DateTimeOffset.Now
        };
        EnsureDefaultVerificationCommands(workspace);

        var projectVm = new ProjectViewModel(workspace);
        Projects.Add(projectVm);
        await _repository.SaveProjectsAsync(Projects.Select(project => project.Project).ToList());
        SelectProject(projectVm);

        // Initialize project (generate AGENTS.md if missing)
        try
        {
            var initializer = new ProjectInitializer();
            await initializer.InitializeProjectAsync(folderPath);
        }
        catch
        {
            // Non-fatal — project still usable without AGENTS.md
        }
    }

    private void OpenRemoveProjectConfirmation(ProjectViewModel? project)
    {
        if (project is null || Projects.Count <= 1)
        {
            return;
        }

        ProjectPendingRemoval = project;
        IsRemoveProjectConfirmationOpen = true;
    }

    private void CloseRemoveProjectConfirmation()
    {
        IsRemoveProjectConfirmationOpen = false;
        ProjectPendingRemoval = null;
    }

    private async Task ConfirmRemoveProjectAsync()
    {
        var project = ProjectPendingRemoval;
        if (project is null || Projects.Count <= 1)
        {
            return;
        }

        var wasSelected = project.IsSelected;
        _inputArtifactFileStore.DeleteStoredFiles(project.Project.InputArtifacts);
        _inputArtifactFileStore.DeleteProjectStore(project.Project.Id);
        Projects.Remove(project);
        CloseRemoveProjectConfirmation();

        if (wasSelected)
        {
            SelectProject(Projects.FirstOrDefault());
        }

        await _repository.SaveProjectsAsync(Projects.Select(project => project.Project).ToList());
        StatusText = "项目已移除";
        RemoveProjectCommand.RaiseCanExecuteChanged();
    }

    private void SelectConversation(ConversationViewModel conversation)
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedConversation, conversation))
        {
            return;
        }

        if (SelectedConversation is not null)
        {
            SelectedConversation.IsSelected = false;
        }

        conversation.IsSelected = true;
        SelectedConversation = conversation;
        Settings.LastActiveConversationId = conversation.Conversation.Id;
        QueueSettingsPersist();
    }

    private void QueueSettingsPersist()
    {
        _settingsPersistCts?.Cancel();
        _settingsPersistCts?.Dispose();
        var cts = new CancellationTokenSource();
        _settingsPersistCts = cts;
        _ = PersistSettingsAfterDelayAsync(cts.Token);
    }

    private async Task PersistSettingsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await PersistSettingsQuietlyAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LoadEarlierMessages()
    {
        SelectedConversation?.LoadEarlierMessages();
        OnPropertyChanged(nameof(HasHiddenMessages));
        OnPropertyChanged(nameof(LoadEarlierMessagesText));
        OnPropertyChanged(nameof(HasMessages));
        LoadEarlierMessagesCommand.RaiseCanExecuteChanged();
    }

    private async void NewChat()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var conversation = SelectedProject.FindUnstartedConversation();
        // Reuse an empty conversation instead of creating many blank rows.
        if (conversation is null)
        {
            conversation = SelectedProject.CreateConversation();
        }

        if (!string.IsNullOrWhiteSpace(ConversationSearchText))
        {
            ConversationSearchText = "";
        }

        SelectConversation(conversation);
        await SaveProjectsAsync();
    }


    private void CopyMessage(ChatMessageViewModel message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            System.Windows.Clipboard.SetText(message.Content);
            StatusText = "消息已复制";
        }
    }

    private void CopyConversationTitle(ConversationViewModel conversation)
    {
        System.Windows.Clipboard.SetText(conversation.Title);
        StatusText = "标题已复制";
    }

    private async Task AttachInputArtifactAsync()
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择输入附件",
            CheckFileExists = true,
            Multiselect = true,
            Filter = "支持的输入|*.txt;*.md;*.json;*.xml;*.yaml;*.yml;*.csv;*.tsv;*.log;*.cs;*.xaml;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.doc;*.docx;*.xlsx;*.xls|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var added = 0;
        var optimized = 0;
        foreach (var fileName in dialog.FileNames)
        {
            try
            {
                var result = await AttachInputArtifactFileAsync(fileName);
                if (result.Added)
                {
                    added++;
                    if (result.Optimized)
                    {
                        optimized++;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"附件读取失败：{Path.GetFileName(fileName)} - {ex.Message}";
            }
        }

        if (added > 0)
        {
            var prunedArtifacts = _inputArtifactService.PruneRemoved(SelectedProject.Project.InputArtifacts);
            _inputArtifactFileStore.DeleteStoredFiles(prunedArtifacts);
            SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            var optimizedText = optimized == 0 ? "" : $"，优化 {optimized} 张图片";
            StatusText = prunedArtifacts.Count == 0
                ? $"已加入 {added} 个输入附件{optimizedText}"
                : $"已加入 {added} 个输入附件{optimizedText}，清理 {prunedArtifacts.Count} 个旧附件";
            OnPropertyChanged(nameof(CurrentInputArtifactSummary));
            RebuildCurrentInputArtifacts();
            UpdateContextUsage();
        }
    }

    public async Task AttachClipboardImageAsync(byte[] imageBytes)
    {
        if (SelectedProject is null || SelectedConversation is null || imageBytes.Length == 0)
        {
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"AIChat-clipboard-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempPath, imageBytes);
            var displayName = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var result = await AttachInputArtifactFileAsync(tempPath, displayName, "clipboard");
            if (!result.Added)
            {
                return;
            }

            var prunedArtifacts = _inputArtifactService.PruneRemoved(SelectedProject.Project.InputArtifacts);
            _inputArtifactFileStore.DeleteStoredFiles(prunedArtifacts);
            SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            StatusText = result.Optimized
                ? "已从剪贴板加入截图附件并优化"
                : "已从剪贴板加入截图附件";
            OnPropertyChanged(nameof(CurrentInputArtifactSummary));
            RebuildCurrentInputArtifacts();
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            StatusText = $"剪贴板图片读取失败：{ex.Message}";
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private async Task<(bool Added, bool Optimized)> AttachInputArtifactFileAsync(
        string fileName,
        string? displayFileName = null,
        string? sourcePathOverride = null)
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return (false, false);
        }

        var fileInfo = new FileInfo(fileName);
        if (!fileInfo.Exists)
        {
            return (false, false);
        }

        var mimeType = GuessMimeType(fileInfo.Extension);
        var preparedImage = InputImageAttachmentOptimizer.Prepare(fileInfo, mimeType);
        var originalDisplayName = string.IsNullOrWhiteSpace(displayFileName)
            ? fileInfo.Name
            : displayFileName.Trim();
        var attachmentFileName = preparedImage.WasOptimized
            ? Path.GetFileNameWithoutExtension(originalDisplayName) + ".jpg"
            : originalDisplayName;
        var attachmentMimeType = preparedImage.MimeType;
        var attachmentSizeBytes = preparedImage.SizeBytes;
        var contentText = ShouldReadText(fileInfo.Extension, mimeType)
            ? await ReadTextPreviewAsync(fileInfo.FullName, 200_000)
            : "";
        var fileBytes = string.IsNullOrWhiteSpace(contentText) && ShouldReadBinaryArtifact(fileInfo.Extension, mimeType, fileInfo.Length)
            ? await File.ReadAllBytesAsync(fileInfo.FullName)
            : [];
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourcePath"] = string.IsNullOrWhiteSpace(sourcePathOverride) ? fileInfo.FullName : sourcePathOverride,
            ["sizeBytes"] = attachmentSizeBytes.ToString()
        };
        if (preparedImage.PixelWidth > 0 && preparedImage.PixelHeight > 0)
        {
            metadata["imageWidth"] = preparedImage.PixelWidth.ToString();
            metadata["imageHeight"] = preparedImage.PixelHeight.ToString();
        }

        if (preparedImage.WasOptimized)
        {
            metadata["optimized"] = "true";
            metadata["originalFileName"] = originalDisplayName;
            metadata["originalSizeBytes"] = preparedImage.OriginalSizeBytes.ToString();
        }

        var artifact = _inputArtifactService.Create(new InputArtifactCreateRequest
        {
            ProjectId = SelectedProject.Project.Id,
            ConversationId = SelectedConversation.Conversation.Id,
            FileName = attachmentFileName,
            MimeType = attachmentMimeType,
            ContentText = contentText,
            FileBytes = fileBytes,
            Metadata = metadata
        });

        if (preparedImage.WasOptimized)
        {
            await _inputArtifactFileStore.StoreBytesAsync(
                artifact,
                preparedImage.OptimizedBytes,
                preparedImage.OptimizedExtension);
        }
        else
        {
            await _inputArtifactFileStore.StoreAsync(artifact, fileInfo.FullName);
        }

        SelectedProject.Project.InputArtifacts.Add(artifact);
        return (true, preparedImage.WasOptimized);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Clipboard image temp cleanup should not hide attachment failures.
        }
    }

    private async Task RemoveInputArtifactAsync(InputArtifactViewModel artifactViewModel)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var removed = SelectedProject.Project.InputArtifacts.Remove(artifactViewModel.Artifact);
        if (!removed)
        {
            var match = SelectedProject.Project.InputArtifacts.FirstOrDefault(artifact =>
                string.Equals(artifact.Id, artifactViewModel.Id, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                removed = SelectedProject.Project.InputArtifacts.Remove(match);
            }
        }

        if (!removed)
        {
            return;
        }

        _inputArtifactFileStore.DeleteStoredFile(artifactViewModel.Artifact);
        if (SelectedInputArtifactPreview?.Id == artifactViewModel.Id)
        {
            CloseInputArtifactPreview();
        }

        SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        StatusText = $"已移除输入附件：{artifactViewModel.FileName}";
        RebuildCurrentInputArtifacts();
        UpdateContextUsage();
    }

    private void OpenInputArtifactPreview(InputArtifactViewModel? artifact)
    {
        if (artifact?.IsImagePreview != true)
        {
            return;
        }

        SelectedInputArtifactPreview = artifact;
        IsInputArtifactPreviewOpen = true;
    }

    private void CloseInputArtifactPreview()
    {
        IsInputArtifactPreviewOpen = false;
        SelectedInputArtifactPreview = null;
    }

    private void RebuildCurrentInputArtifacts()
    {
        CurrentInputArtifacts.Clear();
        if (SelectedProject is null || SelectedConversation is null)
        {
            RaiseInputArtifactProperties();
            return;
        }

        var artifacts = GetCurrentConversationInputArtifacts()
            .Take(8)
            .ToList();
        var visionDecisions = InputArtifactVisionPolicy.Evaluate(artifacts, ActiveModelSupportsVision)
            .ToDictionary(decision => decision.Artifact.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            CurrentInputArtifacts.Add(visionDecisions.TryGetValue(artifact.Id, out var decision)
                ? new InputArtifactViewModel(artifact, decision)
                : new InputArtifactViewModel(artifact, ActiveModelSupportsVision));
        }

        RaiseInputArtifactProperties();
    }

    private void RaiseInputArtifactProperties()
    {
        OnPropertyChanged(nameof(CurrentInputArtifactSummary));
        OnPropertyChanged(nameof(HasCurrentInputArtifacts));
        RemoveInputArtifactCommand.RaiseCanExecuteChanged();
    }

    private InputArtifactDeliverySummary BuildCurrentInputArtifactDeliverySummary(bool modelSupportsVision)
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return InputArtifactDeliverySummary.Empty;
        }

        var artifacts = GetCurrentConversationInputArtifacts().ToList();
        if (artifacts.Count == 0)
        {
            return InputArtifactDeliverySummary.Empty;
        }

        var decisions = InputArtifactVisionPolicy.Evaluate(artifacts, modelSupportsVision);
        var imageCount = decisions.Count(decision => decision.IsImage);
        var sendableImageCount = decisions.Count(decision => decision.CanSend);
        var referencedImageCount = imageCount - sendableImageCount;
        var nonImageCount = artifacts.Count - imageCount;

        var parts = new List<string> { $"{artifacts.Count} 个输入附件" };
        if (sendableImageCount > 0)
        {
            parts.Add($"{sendableImageCount} 张图片将发送");
        }

        if (referencedImageCount > 0)
        {
            parts.Add($"{referencedImageCount} 张图片仅引用");
        }

        if (nonImageCount > 0 && imageCount > 0)
        {
            parts.Add($"{nonImageCount} 个文本/文件引用");
        }

        if (imageCount == 0)
        {
            parts.Add("已加入上下文");
        }

        return new InputArtifactDeliverySummary(
            artifacts.Count,
            imageCount,
            sendableImageCount,
            referencedImageCount,
            string.Join(" · ", parts));
    }

    private IReadOnlyList<InputArtifact> GetCurrentConversationInputArtifacts()
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return [];
        }

        var conversationId = SelectedConversation.Conversation.Id;
        return SelectedProject.Project.InputArtifacts
            .Where(artifact => string.IsNullOrWhiteSpace(artifact.ConversationId) ||
                               string.Equals(artifact.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(artifact => artifact.CreatedAt)
            .ToList();
    }

    private async Task RenameConversationAsync(ConversationViewModel conversation)
    {
        var title = TextPromptDialog.Show(System.Windows.Application.Current.MainWindow, "重命名会话", conversation.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        conversation.Rename(title);
        OnPropertyChanged(nameof(CurrentConversationTitle));
        ApplyConversationFilters();
        await SaveProjectsAsync();
        StatusText = "会话已重命名";
    }

    private static bool ShouldReadText(string extension, string mimeType)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               ext is "txt" or "md" or "json" or "xml" or "yaml" or "yml" or "csv" or "tsv" or "log" or
                   "cs" or "xaml" or "csproj" or "sln" or "props" or "targets";
    }

    private static bool ShouldReadBinaryArtifact(string extension, string mimeType, long sizeBytes)
    {
        const long maxExtractBytes = 8 * 1024 * 1024;
        if (sizeBytes <= 0 || sizeBytes > maxExtractBytes)
        {
            return false;
        }

        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext is "pdf" or "docx" or "xlsx" ||
               mimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
               mimeType.Contains("officedocument", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadTextPreviewAsync(string path, int maxChars)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192);
        var buffer = new char[maxChars + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return new string(buffer, 0, Math.Min(read, maxChars));
    }

    private static string GuessMimeType(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "txt" or "log" => "text/plain",
            "md" => "text/markdown",
            "json" => "application/json",
            "xml" or "xaml" or "csproj" or "props" or "targets" => "application/xml",
            "yaml" or "yml" => "application/yaml",
            "csv" => "text/csv",
            "tsv" => "text/tab-separated-values",
            "cs" => "text/x-csharp",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "pdf" => "application/pdf",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }

    private async Task DeleteConversationAsync(ConversationViewModel conversation)
    {
        var project = Projects.FirstOrDefault(item => item.Conversations.Contains(conversation));
        if (project is null || project.Conversations.Count <= 1)
        {
            StatusText = "至少保留一个对话";
            return;
        }

        project.Conversations.Remove(conversation);
        project.VisibleConversations.Remove(conversation);
        project.Project.Conversations.Remove(conversation.Conversation);
        var removedArtifacts = _inputArtifactService.RemoveForConversation(
            project.Project.InputArtifacts,
            conversation.Conversation.Id);
        _inputArtifactFileStore.DeleteStoredFiles(removedArtifacts);
        if (SelectedConversation == conversation)
        {
            SelectConversation(project.Conversations.First());
        }

        RebuildCurrentInputArtifacts();
        await SaveProjectsAsync();
        StatusText = "对话已删除";
    }

    private void ApplyConversationFilters()
    {
        foreach (var project in Projects)
        {
            project.ApplyConversationFilter(ConversationSearchText);
        }
    }

    private void ApplyConversationFiltersIfSearching()
    {
        if (!string.IsNullOrWhiteSpace(ConversationSearchText))
        {
            ApplyConversationFilters();
        }
    }

    private void RaiseProjectLoadSnapshotProperties()
    {
        _projectLoadSnapshot = SelectedProject is null
            ? new ProjectLoadSnapshot("健康：未选择项目", "画像：无", "活动：无", "建议：先添加或选择一个项目。")
            : ProjectLoadSnapshotBuilder.Build(SelectedProject.Project);
        OnPropertyChanged(nameof(CurrentProjectHealthText));
        OnPropertyChanged(nameof(CurrentProjectProfileText));
        OnPropertyChanged(nameof(CurrentProjectActivityText));
        OnPropertyChanged(nameof(CurrentProjectRecommendationText));
    }

}

internal sealed record InputArtifactDeliverySummary(
    int TotalCount,
    int ImageCount,
    int SendableImageCount,
    int ReferencedImageCount,
    string SummaryText)
{
    public static InputArtifactDeliverySummary Empty { get; } = new(0, 0, 0, 0, "");
}
