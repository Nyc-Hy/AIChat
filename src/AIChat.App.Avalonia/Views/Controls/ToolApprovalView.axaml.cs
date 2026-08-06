using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.Views.Controls;

// Tool approval modal (window-modal). Blocks the rest of the
// window so the user can't queue another prompt while a write
// is pending. The agent loop is awaiting on the
// PresentRequestAsync TCS — without this modal the run would
// hang forever on the first write_file.
//
// Three buttons: Reject (cancel this single call), Approve
// (one-shot allow), Approve for session (allow this and any
// same-shape tool for the rest of the run). The scrim click
// rejects — clicking outside the dialog is functionally a "no,
// don't do that" gesture, and the agent loop's
// PresentRequestAsync is still awaiting on the TCS so this
// resolves it with a Reject and the run ends.
//
// Keyboard shortcuts: Esc = Reject, Enter = Approve one-shot.
// Approve-for-session deliberately has no shortcut (it's the
// less-common choice, and binding a third key would conflict
// with the "S to send" muscle memory users build up after a
// few prompts).
public partial class ToolApprovalView : UserControl
{
    public ToolApprovalView()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(() =>
            {
                if (DataContext is MainWindowViewModel vm &&
                    vm.Approval.RejectCommand.CanExecute(null))
                {
                    vm.Approval.RejectCommand.Execute(null);
                }
            })
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Enter),
            Command = new RelayCommand(() =>
            {
                if (DataContext is MainWindowViewModel vm &&
                    vm.Approval.ApproveCommand.CanExecute(null))
                {
                    vm.Approval.ApproveCommand.Execute(null);
                }
            })
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ToolApprovalScrim_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.Approval.RejectCommand.CanExecute(null))
        {
            viewModel.Approval.RejectCommand.Execute(null);
        }
    }

    private void ToolApprovalContent_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
