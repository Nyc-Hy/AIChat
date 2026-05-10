using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public ContextUsage ContextUsage
    {
        get => _contextUsage;
        private set
        {
            if (SetProperty(ref _contextUsage, value))
            {
                // The ring, tooltip, labels, and warnings are all projections of
                // the same ContextUsage value.
                OnPropertyChanged(nameof(ContextPercent));
                OnPropertyChanged(nameof(ConversationUsagePercent));
                OnPropertyChanged(nameof(ConversationRemainingPercent));
                OnPropertyChanged(nameof(ContextWindowSummary));
                OnPropertyChanged(nameof(ContextTokenSummary));
                OnPropertyChanged(nameof(ContextTooltip));
                OnPropertyChanged(nameof(ContextLabel));
                OnPropertyChanged(nameof(ContextCompressionHint));
            }
        }
    }

    public double ContextPercent => ContextUsage.Ratio * 100;
    public double ConversationUsagePercent => ContextUsage.ConversationLimit <= 0
        ? 0
        : Math.Clamp((double)ContextUsage.CurrentTokens / ContextUsage.ConversationLimit * 100, 0, 100);
    public double ConversationRemainingPercent => Math.Max(0, 100 - ConversationUsagePercent);
    public string ContextLabel => $"{ContextUsage.CurrentTokens / 1000.0:0.#}K";
    public string ContextWindowSummary => $"{ConversationUsagePercent:0.#}% 已用（剩余 {ConversationRemainingPercent:0.#}%）";
    public string ContextTokenSummary => $"已用 {ContextUsage.CurrentTokens:N0} tokens，共 {ContextUsage.ConversationLimit:N0}";
    public string ContextCompressionHint => ConversationUsagePercent >= 85
        ? "接近上限时将自动压缩背景信息"
        : "AIChat 会自动保留可用背景信息";
    public string ContextTooltip =>
        $"背景信息窗口：{ConversationUsagePercent:0.#}% 已用（剩余 {ConversationRemainingPercent:0.#}%）\n" +
        $"已用 {ContextUsage.CurrentTokens:N0} tokens，共 {ContextUsage.ConversationLimit:N0}\n" +
        ContextCompressionHint;

    private void UpdateContextUsage()
    {
        ApplySelectedConfiguredProvider();
        var conversation = SelectedConversation?.Conversation;
        var settings = Settings;
        var revision = Interlocked.Increment(ref _contextUsageRevision);

        _contextUsageCts?.Cancel();
        _contextUsageCts?.Dispose();

        if (conversation is null)
        {
            _contextUsageCts = null;
            ContextUsage = CreateEmptyContextUsage(settings);
            return;
        }

        if (_contextUsageCache.TryGetValue(conversation.Id, out var cachedUsage))
        {
            ContextUsage = cachedUsage;
        }
        else
        {
            ContextUsage = CreateEmptyContextUsage(settings);
        }

        var cts = new CancellationTokenSource();
        _contextUsageCts = cts;
        _ = UpdateContextUsageAsync(conversation, settings, revision, cts.Token);
    }

    private static ContextUsage CreateEmptyContextUsage(AppSettings settings)
    {
        return new ContextUsage
        {
            CurrentTokens = 0,
            ConversationLimit = Math.Min(settings.ModelContextLimit, 64_000),
            ModelLimit = settings.ModelContextLimit
        };
    }

    private async Task UpdateContextUsageAsync(
        Conversation conversation,
        AppSettings settings,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            var usage = await Task.Run(() =>
            {
                var messages = conversation.Messages.ToList();
                cancellationToken.ThrowIfCancellationRequested();

                var estimatedUsage = _fastContextEstimator.Estimate(messages, settings);
                if (!settings.UseTokenizerEstimation || messages.Count == 0)
                {
                    return estimatedUsage;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return _contextEstimator.Estimate(messages, settings);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested || revision != _contextUsageRevision)
            {
                return;
            }

            await InvokeOnUiAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && revision == _contextUsageRevision)
                {
                    _contextUsageCache[conversation.Id] = usage;
                    ContextUsage = usage;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The fast estimate is already visible; tokenizer refinement is best-effort.
        }
    }

    private async Task AppendAssistantContentAsync(ChatMessageViewModel assistantViewModel, string content, CancellationToken cancellationToken)
    {
        const int chunkSize = 24;
        if (content.Length <= chunkSize)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                assistantViewModel.Content += content;
                UpdateContextUsage();
                ApplyConversationFiltersIfSearching();
            });
            return;
        }

        for (var index = 0; index < content.Length; index += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(chunkSize, content.Length - index);
            var chunk = content.Substring(index, length);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // All bound UI changes must happen on the WPF dispatcher thread.
                assistantViewModel.Content += chunk;
                UpdateContextUsage();
                ApplyConversationFiltersIfSearching();
            });
            await Task.Delay(12, cancellationToken);
        }
    }
}
