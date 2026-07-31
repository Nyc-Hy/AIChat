using AIChat.App.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

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
public partial class ToolApprovalView : UserControl
{
    public ToolApprovalView()
    {
        InitializeComponent();
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
