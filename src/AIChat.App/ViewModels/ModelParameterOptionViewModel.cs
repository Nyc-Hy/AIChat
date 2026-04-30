using System.Collections.ObjectModel;
using AIChat.Abstractions.Llm;

namespace AIChat.App.ViewModels;

public sealed class ModelParameterOptionViewModel : ObservableObject
{
    private string _selectedValue = "";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public ObservableCollection<LlmParameterOption> Options { get; } = [];

    public string SelectedValue
    {
        get => _selectedValue;
        set => SetProperty(ref _selectedValue, value);
    }
}
