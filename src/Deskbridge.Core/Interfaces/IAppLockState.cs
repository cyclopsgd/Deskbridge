namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 6 Plan 06-03 scaffolding (Plan 06-04 completion): read/write surface for
/// the app lock state. Gates the Ctrl+Shift+P palette (CMD-01 / Q6) and any future
/// lock-aware UI element. Plan 06-04 wires <see cref="Lock"/> / <see cref="Unlock"/>
/// from the master-password verification flow.
/// </summary>
public interface IAppLockState
{
    /// <summary>True when the app is in the locked state (master-password required).</summary>
    bool IsLocked { get; }

    /// <summary>
    /// Raised when <see cref="IsLocked"/> changes. Bool payload is the NEW value
    /// (true = just locked, false = just unlocked). Subscribers may observe both
    /// transitions. No-op suppressing rules live inside the implementation.
    /// </summary>
    event EventHandler<bool>? LockStateChanged;

    /// <summary>
    /// Transition to locked. Idempotent — calling while already locked is a no-op
    /// and does NOT re-raise <see cref="LockStateChanged"/>.
    /// </summary>
    void Lock();

    /// <summary>
    /// Transition to unlocked. Idempotent — calling while already unlocked is a no-op
    /// and does NOT re-raise <see cref="LockStateChanged"/>.
    /// </summary>
    void Unlock();
}
