using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 6 Plan 06-03 scaffolding (Plan 06-04 consumer): default <see cref="IAppLockState"/>.
/// <see cref="IsLocked"/> starts <c>false</c>; Plan 06-04 will invoke <see cref="Lock"/>
/// on startup before master-password verification and <see cref="Unlock"/> on success.
/// </summary>
public sealed class AppLockState : IAppLockState
{
    public bool IsLocked { get; private set; }

    public event EventHandler<bool>? LockStateChanged;

    public void Lock()
    {
        if (IsLocked) return;
        IsLocked = true;
        LockStateChanged?.Invoke(this, true);
    }

    public void Unlock()
    {
        if (!IsLocked) return;
        IsLocked = false;
        LockStateChanged?.Invoke(this, false);
    }
}
