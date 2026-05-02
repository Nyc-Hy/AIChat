using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIChat.App.ViewModels;
using AIChat.Application.Agents;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Providers.Anthropic;
using AIChat.Providers.OpenAI;
using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.App;

// Main window code-behind stays focused on WPF-only concerns: constructing the
// ViewModel, keyboard shortcuts, password box synchronization, scrolling, and
// custom window buttons.
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private INotifyCollectionChanged? _hookedMessages;
    private bool _isSyncingPasswordBox;

    public MainWindow()
    {
        InitializeComponent();
        // Composition root for the MVP. Dependencies are wired here manually so
        // the learning path is visible before introducing a DI container.
        var chatService = new RoutedChatCompletionService(
        [
            new OpenAICompatibleChatProvider(),
            new AnthropicChatProvider()
        ]);
        var toolRegistry = AgentToolRegistry.CreateDefault();
        var contextEstimator = new TokenizerContextEstimator();
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIChat");
        var auditLogRepository = new AuditLogRepository(appDataPath);
        _viewModel = new MainViewModel(
            new JsonAppRepository(),
            chatService,
            contextEstimator,
            new ConversationContextBuilder(
                contextEstimator,
                new SystemPromptBuilder()),
            new WorkspaceChangeService(),
            auditLogRepository);
        var toolCatalog = new AgentToolCatalog(toolRegistry.All);
        var agentRunner = new AgentRunner(chatService, toolCatalog);
        _viewModel.ConfigureAgent(
            new AgentHarness(agentRunner),
            toolRegistry);
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Async initialization is delayed until Loaded so bindings and controls
        // already exist when the ViewModel starts raising notifications.
        await _viewModel.InitializeAsync();
        HookMessageCollection();
    }

    private async void ComposerBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Enter sends the current draft, matching common chat/editor behavior.
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (_viewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            _viewModel.SendCommand.Execute(null);
            await Dispatcher.InvokeAsync(ScrollMessagesToEnd);
        }
    }

    private void HookMessageCollection()
    {
        // Message collections change when switching conversations or streaming
        // responses, so the window re-hooks events to keep auto-scroll working.
        if (_hookedMessages is not null)
        {
            _hookedMessages.CollectionChanged -= Messages_CollectionChanged;
        }

        if (_viewModel.Messages is INotifyCollectionChanged collection)
        {
            _hookedMessages = collection;
            collection.CollectionChanged += Messages_CollectionChanged;
            foreach (var message in _viewModel.Messages)
            {
                message.PropertyChanged -= Message_PropertyChanged;
                message.PropertyChanged += Message_PropertyChanged;
            }
        }

        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.Messages))
            {
                HookMessageCollection();
                Dispatcher.InvokeAsync(ScrollMessagesToEnd);
            }

            if (args.PropertyName == nameof(MainViewModel.NewProviderApiKey))
            {
                SyncPasswordBoxFromViewModel();
            }
        };
    }

    private void NewProviderPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password is not a normal bindable dependency property, so
        // code-behind performs a careful two-way sync with the ViewModel.
        if (_isSyncingPasswordBox)
        {
            return;
        }

        _viewModel.NewProviderApiKey = NewProviderPasswordBox.Password;
    }

    private void SyncPasswordBoxFromViewModel()
    {
        if (NewProviderPasswordBox.Password == _viewModel.NewProviderApiKey)
        {
            return;
        }

        _isSyncingPasswordBox = true;
        NewProviderPasswordBox.Password = _viewModel.NewProviderApiKey;
        _isSyncingPasswordBox = false;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ChatMessageViewModel>())
            {
                item.PropertyChanged -= Message_PropertyChanged;
                item.PropertyChanged += Message_PropertyChanged;
            }
        }

        Dispatcher.InvokeAsync(ScrollMessagesToEnd);
    }

    private void Message_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Streaming updates mutate the last assistant message many times; each
        // content change should keep the viewport pinned to the newest text.
        if (e.PropertyName == nameof(ChatMessageViewModel.Content))
        {
            Dispatcher.InvokeAsync(ScrollMessagesToEnd);
        }
    }

    private void ScrollMessagesToEnd()
    {
        MessagesScrollViewer.ScrollToEnd();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ensure the ScrollViewer scrolls even when it doesn't have keyboard focus.
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
