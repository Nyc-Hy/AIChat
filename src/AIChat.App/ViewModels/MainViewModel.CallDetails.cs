using System.Collections.ObjectModel;
using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<LlmCallDetailViewModel>? CurrentCallDetails => _callDetailsConversation?.CallDetails;
    public string CallDetailsTitle => _callDetailsConversation is null
        ? "调用详情"
        : $"{_callDetailsConversation.Title} · 调用详情";

    public LlmCallDetailViewModel? SelectedCallDetail
    {
        get => _selectedCallDetail;
        set
        {
            if (SetProperty(ref _selectedCallDetail, value))
            {
                _ = LoadSelectedCallDetailJsonAsync(value);
            }
        }
    }
    public string SelectedCallRequestJson
    {
        get => _selectedCallRequestJson;
        private set => SetProperty(ref _selectedCallRequestJson, value);
    }

    public string SelectedCallResponseJson
    {
        get => _selectedCallResponseJson;
        private set => SetProperty(ref _selectedCallResponseJson, value);
    }

    public bool ShowSelectedCallRawEvents
    {
        get => _showSelectedCallRawEvents;
        set
        {
            if (SetProperty(ref _showSelectedCallRawEvents, value))
            {
                _ = LoadSelectedCallDetailJsonAsync(SelectedCallDetail);
            }
        }
    }

    private void OpenCallDetails(ConversationViewModel conversation)
    {
        // The inspector is tied to one conversation at a time and reads its saved
        // request/response snapshots.
        _callDetailsConversation = conversation;
        SelectedCallDetail = null;
        ShowSelectedCallRawEvents = false;
        SelectedCallRequestJson = conversation.CallDetails.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
        SelectedCallResponseJson = conversation.CallDetails.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
        OnPropertyChanged(nameof(CurrentCallDetails));
        OnPropertyChanged(nameof(CallDetailsTitle));
        IsCallDetailsOpen = true;
    }

    private async Task CompleteCallDetailAsync(LlmCallDetail detail, string status, object response)
    {
        // JSON formatting can be a little expensive for large raw event lists, so
        // run it off the UI thread.
        var responseJson = await Task.Run(() => SerializeJson(response));
        detail.Status = status;
        detail.CompletedAt = DateTimeOffset.Now;
        detail.ResponseJson = responseJson;
        SelectedConversation?.RefreshCallDetail(detail);
        if (SelectedCallDetail?.Detail.Id == detail.Id)
        {
            SelectedCallDetail = new LlmCallDetailViewModel(detail);
        }
    }

    private async Task LoadSelectedCallDetailJsonAsync(LlmCallDetailViewModel? detail)
    {
        var version = ++_callDetailLoadVersion;
        if (detail is null)
        {
            SelectedCallRequestJson = CurrentCallDetails?.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
            SelectedCallResponseJson = CurrentCallDetails?.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
            return;
        }

        SelectedCallRequestJson = "正在加载入参 JSON...";
        SelectedCallResponseJson = "正在加载出参 JSON...";
        var result = await Task.Run(() => new
        {
            RequestJson = detail.RequestJson,
            ResponseJson = ShowSelectedCallRawEvents ? detail.ResponseJsonWithRawEvents : detail.ResponseJson
        });

        if (version != _callDetailLoadVersion)
        {
            // User selected another call while this one was formatting.
            return;
        }

        SelectedCallRequestJson = result.RequestJson;
        SelectedCallResponseJson = result.ResponseJson;
    }

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, DetailJsonOptions);
    }

    private static IReadOnlyList<object> NormalizeRawJsonEvents(IEnumerable<string> rawEvents)
    {
        // Store parsed JSON when possible so the inspector can pretty-print it.
        var normalized = new List<object>();
        foreach (var rawEvent in rawEvents)
        {
            if (string.IsNullOrWhiteSpace(rawEvent))
            {
                continue;
            }

            if (rawEvent == "[DONE]")
            {
                normalized.Add(rawEvent);
                continue;
            }

            try
            {
                normalized.Add(JsonSerializer.Deserialize<JsonElement>(rawEvent));
            }
            catch (JsonException)
            {
                normalized.Add(rawEvent);
            }
        }

        return normalized;
    }
}
