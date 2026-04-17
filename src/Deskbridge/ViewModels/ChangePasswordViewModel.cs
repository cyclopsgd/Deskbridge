using CommunityToolkit.Mvvm.Input;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6.1: ViewModel for <see cref="Dialogs.ChangePasswordDialog"/>. Handles the
/// current/new/confirm flow for both password and PIN modes. Mode is determined at
/// construction time from <see cref="IMasterPasswordService.GetAuthMode"/>. On success
/// the new hash is written via <see cref="IMasterPasswordService.SetMasterPassword(string, string)"/>
/// and all fields are scrubbed (T-6.1-02).
/// </summary>
public partial class ChangePasswordViewModel : ObservableObject
{
    private readonly IMasterPasswordService _masterPassword;

    public ChangePasswordViewModel(IMasterPasswordService masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        _masterPassword = masterPassword;
        IsPinMode = masterPassword.GetAuthMode() == "pin";
    }

    [ObservableProperty]
    public partial bool IsPinMode { get; set; }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = "";

    [ObservableProperty]
    public partial string NewPassword { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmNewPassword { get; set; } = "";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    public string Title => IsPinMode ? "Change PIN" : "Change Password";
    public string CurrentLabel => IsPinMode ? "Current PIN" : "Current password";
    public string NewLabel => IsPinMode ? "New PIN" : "New password";
    public string ConfirmLabel => IsPinMode ? "Confirm new PIN" : "Confirm new password";

    [RelayCommand]
    public void Submit()
    {
        ErrorMessage = null;
        IsSuccess = false;

        // Validate current
        if (!_masterPassword.VerifyMasterPassword(CurrentPassword))
        {
            ErrorMessage = IsPinMode ? "Incorrect PIN." : "Incorrect password.";
            return;
        }

        // Validate new
        if (IsPinMode)
        {
            if (NewPassword.Length != 6 || !NewPassword.All(char.IsDigit))
            {
                ErrorMessage = "PIN must be exactly 6 digits.";
                return;
            }
        }
        else
        {
            if (NewPassword.Length < 8)
            {
                ErrorMessage = "Password must be at least 8 characters.";
                return;
            }
        }

        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            ErrorMessage = IsPinMode ? "PINs do not match." : "Passwords do not match.";
            return;
        }

        _masterPassword.SetMasterPassword(NewPassword, IsPinMode ? "pin" : "password");

        // T-6.1-02: scrub all fields after success
        CurrentPassword = "";
        NewPassword = "";
        ConfirmNewPassword = "";
        IsSuccess = true;
    }
}
