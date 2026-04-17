using System.Windows;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Dialogs;
using Serilog;
using Wpf.Ui;

namespace Deskbridge.Services;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-02 / SEC-04 / SEC-05, Pitfall 5 Option A) orchestrator
/// that coordinates the lock/unlock flow:
///
/// <list type="number">
/// <item>Subscribes to <see cref="AppLockedEvent"/> on the bus so idle-timer,
/// session-switch, Ctrl+L, and minimise-to-lock all reach the same controller.</item>
/// <item>On lock: snapshots every <see cref="IHostContainerProvider.HostContainer"/>
/// child's <see cref="UIElement.Visibility"/> into a dict (Pitfall 5 Option A),
/// sets all to <see cref="Visibility.Collapsed"/>, flips <see cref="IAppLockState"/>,
/// logs an audit record, and shows <see cref="LockOverlayDialog"/>.</item>
/// <item>On successful unlock (from <see cref="LockOverlayViewModel.UnlockSucceeded"/>):
/// restores each child's prior Visibility (NOT all-Visible — the lock captured
/// per-child state), flips <see cref="IAppLockState"/>, publishes
/// <see cref="AppUnlockedEvent"/>, logs audit.</item>
/// </list>
///
/// <para><b>Pitfall 5 (Option A)</b>: WPF overlays including
/// <see cref="Wpf.Ui.Controls.ContentDialog"/> are painted by the WPF visual
/// tree, but <see cref="System.Windows.Forms.Integration.WindowsFormsHost"/>
/// contents render via OS compositing on top — so a lock overlay would NOT
/// hide RDP pixels underneath. Option A collapses every WFH before showing the
/// overlay and restores them on unlock. Sessions stay connected in the
/// background (Option B rejected — disconnecting loses in-flight work).</para>
///
/// <para><b>D-18 idempotency</b>: <see cref="LockAsync"/> is a no-op when
/// already locked (the VM command may fire again via rapid Ctrl+L presses, or
/// a SessionSwitch may arrive during a timer-triggered lock). <see cref="UnlockAsync"/>
/// is a no-op when already unlocked so a stray callback can't flip us to
/// unlocked with an empty dict.</para>
///
/// <para>No ctor-time reference to the Window itself — we use
/// <see cref="IHostContainerProvider"/> so tests can inject a surrogate Panel.</para>
/// </summary>
public sealed class AppLockController
{
    private readonly IAppLockState _lockState;
    private readonly IEventBus _bus;
    private readonly IContentDialogService _dialogs;
    private readonly IAuditLogger _audit;
    private readonly Func<LockOverlayDialog> _dialogFactory;
    private readonly IHostContainerProvider _host;
    private readonly IMasterPasswordService _masterPassword;

    /// <summary>
    /// Phase 6.1: mutable so the confirmation dialog flow can update it at runtime
    /// when the user disables/re-enables password protection. IdleLockService and
    /// SessionLockService hold startup-time values and require app restart for
    /// re-activation of timers/subscriptions.
    /// </summary>
    private bool _requireMasterPassword;

    // Pitfall 5 Option A: snapshot of every HostContainer child's Visibility
    // at lock time. Restored per-child on unlock so we don't flip a previously-
    // Collapsed WFH (e.g. an inactive tab) to Visible on unlock.
    private readonly Dictionary<UIElement, Visibility> _preLockVisibility = new();

    // Tracks whether an overlay is currently displayed so repeat LockAsync
    // calls from different triggers (timer + SessionSwitch + Ctrl+L) don't
    // stack dialogs.
    private LockOverlayDialog? _activeDialog;

    public AppLockController(
        IAppLockState lockState,
        IEventBus bus,
        IContentDialogService dialogs,
        IAuditLogger audit,
        Func<LockOverlayDialog> dialogFactory,
        IHostContainerProvider host,
        IMasterPasswordService masterPassword,
        bool requireMasterPassword = true)
    {
        ArgumentNullException.ThrowIfNull(lockState);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(dialogFactory);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(masterPassword);

        _lockState = lockState;
        _bus = bus;
        _dialogs = dialogs;
        _audit = audit;
        _dialogFactory = dialogFactory;
        _host = host;
        _masterPassword = masterPassword;
        _requireMasterPassword = requireMasterPassword;

        // Bus subscription: IdleLockService + SessionLockService publish
        // AppLockedEvent with the matching LockReason; we fan in here.
        _bus.Subscribe<AppLockedEvent>(this, e => _ = LockAsync(e.Reason));
    }

    /// <summary>
    /// Transitions the app to the locked state: Pitfall 5 airspace capture,
    /// IAppLockState flip, audit log, show lock overlay. Idempotent.
    /// </summary>
    public async Task LockAsync(LockReason reason)
    {
        if (!_requireMasterPassword) return; // Phase 6.1: password disabled — no-op
        if (_lockState.IsLocked) return; // D-18 idempotent

        CaptureAndCollapseHosts();

        _lockState.Lock();

        try
        {
            await _audit.LogAsync(new AuditRecord(
                Ts: DateTime.UtcNow.ToString("O"),
                Type: AuditAction.AppLocked.ToString(),
                ConnectionId: null,
                User: Environment.UserName,
                Outcome: "success",
                ErrorCode: reason.ToString()));
        }
        catch (Exception ex)
        {
            // Audit failure must NEVER prevent the lock itself — security > observability.
            Log.Warning(ex, "Failed to write AppLocked audit record — continuing lock flow");
        }

        await ShowLockOverlayAsync();
    }

    /// <summary>
    /// Transitions the app to the unlocked state: restore per-child Visibility
    /// from the snapshot, IAppLockState flip, audit log, publish AppUnlockedEvent.
    /// Idempotent.
    /// </summary>
    public async Task UnlockAsync()
    {
        if (!_lockState.IsLocked) return;

        RestoreHostVisibility();

        _lockState.Unlock();
        _bus.Publish(new AppUnlockedEvent());

        try
        {
            await _audit.LogAsync(new AuditRecord(
                Ts: DateTime.UtcNow.ToString("O"),
                Type: AuditAction.AppUnlocked.ToString(),
                ConnectionId: null,
                User: Environment.UserName,
                Outcome: "success"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write AppUnlocked audit record");
        }
    }

    /// <summary>
    /// Phase 6.1: allows the MainWindow toggle confirmation flow to update the
    /// controller's guard at runtime without requiring an app restart. IdleLockService
    /// and SessionLockService still hold startup-time values (require restart).
    /// </summary>
    public bool RequireMasterPassword
    {
        get => _requireMasterPassword;
        set => _requireMasterPassword = value;
    }

    /// <summary>
    /// Called when the user disables password protection via the settings toggle.
    /// Ensures no lingering lock state remains: dismisses any active lock overlay
    /// dialog, restores HostContainer children visibility from the snapshot, and
    /// clears <see cref="IAppLockState.IsLocked"/>. Idempotent — safe to call when
    /// already unlocked or when no dialog is active.
    ///
    /// <para>Without this, disabling the password while a lock overlay or collapsed
    /// hosts exist leaves an invisible blocking layer (the ContentDialog SmokeGrid
    /// and/or collapsed WFH children persist with no unlock path).</para>
    /// </summary>
    public async Task ForceDisableAsync()
    {
        _requireMasterPassword = false;

        // Dismiss any active lock overlay dialog so its SmokeGrid is removed.
        if (_activeDialog is { } dialog)
        {
            dialog.Hide();
            // ShowAsync will complete and the finally block will null _activeDialog.
        }

        // Restore host visibility if we captured a snapshot during a prior lock.
        RestoreHostVisibility();

        // Clear locked state if set.
        if (_lockState.IsLocked)
        {
            _lockState.Unlock();
            _bus.Publish(new AppUnlockedEvent());

            try
            {
                await _audit.LogAsync(new AuditRecord(
                    Ts: DateTime.UtcNow.ToString("O"),
                    Type: AuditAction.AppUnlocked.ToString(),
                    ConnectionId: null,
                    User: Environment.UserName,
                    Outcome: "success",
                    ErrorCode: "PasswordDisabled"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write AppUnlocked audit record after password disable");
            }
        }
    }

    /// <summary>
    /// Called from <c>App.OnStartup</c> after <c>mainWindow.Show()</c>. If
    /// <c>auth.json</c> exists (returning user), enters unlock mode. If not
    /// (first-run), the same <see cref="LockOverlayDialog"/> will render in
    /// setup mode via <see cref="LockOverlayViewModel.IsFirstRun"/>.
    /// Either way the app is gated until the user authenticates.
    /// </summary>
    public Task EnsureLockedOnStartupAsync()
    {
        // Use Manual as the startup lock reason — the audit entry carries the
        // same shape as the user pressing Ctrl+L. A dedicated Startup reason
        // was rejected because it would grow LockReason without audit value.
        return LockAsync(LockReason.Manual);
    }

    // --------------------------------------------------------------------
    // Pitfall 5 airspace mitigation
    // --------------------------------------------------------------------

    private void CaptureAndCollapseHosts()
    {
        _preLockVisibility.Clear();
        var children = _host.HostContainer.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is UIElement child)
            {
                _preLockVisibility[child] = child.Visibility;
                child.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void RestoreHostVisibility()
    {
        foreach (var kvp in _preLockVisibility)
        {
            // Defensive: if the child was removed from HostContainer while
            // the app was locked (e.g. tab closed), skip the restore.
            kvp.Key.Visibility = kvp.Value;
        }
        _preLockVisibility.Clear();
    }

    // --------------------------------------------------------------------
    // Dialog lifecycle
    // --------------------------------------------------------------------

    private async Task ShowLockOverlayAsync()
    {
        if (_activeDialog is not null)
        {
            // Another trigger raised LockAsync while the dialog was already
            // open. The overlay covers the shell; nothing to do.
            return;
        }

        try
        {
            var dialog = _dialogFactory();
            _activeDialog = dialog;

            // Subscribe BEFORE ShowAsync so we can't race against a successful
            // unlock that happens before Subscribe runs (ShowAsync yields).
            EventHandler? handler = null;
            handler = async (_, _) =>
            {
                dialog.ViewModel.UnlockSucceeded -= handler;
                try
                {
                    await UnlockAsync();
                }
                finally
                {
                    dialog.Hide();
                }
            };
            dialog.ViewModel.UnlockSucceeded += handler;

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show LockOverlayDialog");
        }
        finally
        {
            _activeDialog = null;
        }
    }
}
