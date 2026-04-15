using System.Windows.Controls;

namespace Deskbridge.Services;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-02, Pitfall 5 airspace): lightweight surface for
/// <see cref="AppLockController"/> to reach the <see cref="MainWindow"/>'s
/// <c>HostContainer</c> Grid (the single parent of every live
/// <see cref="System.Windows.Forms.Integration.WindowsFormsHost"/> — per
/// Phase 5 D-04 / WINFORMS-HOST-AIRSPACE §Option 3).
///
/// <para>Extracted as an interface so <see cref="AppLockController"/> doesn't
/// take a direct reference to <c>MainWindow</c> (which would complicate
/// testing). <c>MainWindow</c> implements this by returning the Grid via
/// <c>FindName("HostContainer")</c>. Tests can implement it with a surrogate
/// Grid pre-populated with <see cref="WindowsFormsHost"/> children.</para>
/// </summary>
public interface IHostContainerProvider
{
    /// <summary>
    /// The Panel whose children are the live <see cref="WindowsFormsHost"/>
    /// instances. Lock = snapshot + collapse every child; Unlock = restore
    /// per-child prior Visibility from the snapshot. Do NOT iterate
    /// <see cref="Wpf.Ui.ISnackbarService"/> or other WPF-only controls here —
    /// only WFH children create the airspace leak.
    /// </summary>
    Panel HostContainer { get; }
}
