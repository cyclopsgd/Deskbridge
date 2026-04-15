using CommunityToolkit.Mvvm.Input;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01 / SEC-02) ViewModel for
/// <see cref="Deskbridge.Dialogs.LockOverlayDialog"/>. Handles two modes:
///
/// <list type="bullet">
/// <item><b>First-run setup</b> (<see cref="IsFirstRun"/> true) — two password fields,
/// validates minimum length (8) + match, then writes the hash via
/// <see cref="IMasterPasswordService.SetMasterPassword"/> and raises <see cref="UnlockSucceeded"/>.</item>
/// <item><b>Unlock</b> (<see cref="IsFirstRun"/> false) — single password field,
/// calls <see cref="IMasterPasswordService.VerifyMasterPassword"/>. On failure shows the
/// UI-SPEC copy, clears the password, and raises <see cref="RequestFocusPassword"/> so
/// the dialog re-focuses the input.</item>
/// </list>
///
/// <para><b>T-06-05:</b> passwords are stored in a plain <see cref="string"/> (SecureString
/// is banned by CLAUDE.md — DE0001). The best available mitigation is tight lifetime scope:
/// <see cref="Unlock"/> clears <see cref="Password"/> and <see cref="ConfirmPassword"/>
/// immediately after the KDF call returns so the GC can reclaim the backing chars.</para>
///
/// <para>Copy strings are pulled verbatim from UI-SPEC §Lock Overlay Copywriting (lines 398-420)
/// — any change should update the UI-SPEC and the test assertions together.</para>
/// </summary>
public partial class LockOverlayViewModel : ObservableObject
{
    private readonly IMasterPasswordService _masterPassword;

    /// <summary>Raised when the user's credentials are accepted. The dialog hides itself on this signal.</summary>
    public event EventHandler? UnlockSucceeded;

    /// <summary>Raised after a failed unlock so the code-behind can re-focus the password field.</summary>
    public event EventHandler? RequestFocusPassword;

    public LockOverlayViewModel(IMasterPasswordService masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        _masterPassword = masterPassword;
        IsFirstRun = !masterPassword.IsMasterPasswordSet();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyCopy))]
    [NotifyPropertyChangedFor(nameof(ButtonCopy))]
    [NotifyPropertyChangedFor(nameof(PasswordPlaceholder))]
    public partial bool IsFirstRun { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = "";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // -------------- copy (UI-SPEC §Lock Overlay Copywriting lines 398-420) --------------

    public string BodyCopy => IsFirstRun
        ? "Set a master password to protect your connections. This password cannot be recovered — choose something memorable."
        : "Locked. Enter your master password to continue.";

    public string ButtonCopy => IsFirstRun ? "Set Password" : "Unlock";

    public string PasswordPlaceholder => IsFirstRun ? "New master password" : "Master password";

    // -------------- command --------------

    /// <summary>
    /// First-run path: validates length ≥8 + match, writes hash, raises UnlockSucceeded.
    /// Unlock path: verifies against stored hash, raises UnlockSucceeded on match.
    /// On validation failure sets <see cref="ErrorMessage"/> to UI-SPEC copy and leaves
    /// the state untouched so the user can retry.
    /// </summary>
    [RelayCommand]
    public void Unlock()
    {
        ErrorMessage = null;

        if (IsFirstRun)
        {
            if (Password.Length < 8)
            {
                ErrorMessage = "Password must be at least 8 characters.";
                return;
            }
            if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }
            _masterPassword.SetMasterPassword(Password);

            // T-06-05: scrub in-memory password ASAP — best-effort since SecureString is banned.
            Password = "";
            ConfirmPassword = "";
            UnlockSucceeded?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_masterPassword.VerifyMasterPassword(Password))
        {
            ErrorMessage = "Incorrect password. Try again.";
            Password = "";
            RequestFocusPassword?.Invoke(this, EventArgs.Empty);
            return;
        }

        Password = "";
        UnlockSucceeded?.Invoke(this, EventArgs.Empty);
    }
}
