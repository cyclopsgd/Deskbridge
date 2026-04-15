using Deskbridge.ViewModels;

namespace Deskbridge.Controls;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-01 / D-07): code-behind for the custom toast stack.
/// Wires hover-pause — <c>MouseEnter</c> pauses every auto-dismiss timer in the
/// bound <see cref="ToastStackViewModel"/>, <c>MouseLeave</c> resumes them.
/// All semantic state lives in the VM; the control is a thin visual host.
/// </summary>
public partial class ToastStackControl : UserControl
{
    public ToastStackControl()
    {
        InitializeComponent();

        MouseEnter += (_, _) => (DataContext as ToastStackViewModel)?.Pause();
        MouseLeave += (_, _) => (DataContext as ToastStackViewModel)?.Resume();
    }
}
