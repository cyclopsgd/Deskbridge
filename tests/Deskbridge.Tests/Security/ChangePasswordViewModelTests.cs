using Deskbridge.Core.Interfaces;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6.1: behavioural tests for <see cref="ChangePasswordViewModel"/>.
/// Validates current password verification, new password/PIN validation per mode,
/// SetMasterPassword delegation, field scrub (T-6.1-02), and mode-aware copy.
/// </summary>
public sealed class ChangePasswordViewModelTests
{
    // --------------------------------------------------------------------
    // Test 1 — Wrong current password shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_WrongCurrent_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("password");
        svc.VerifyMasterPassword("wrong").Returns(false);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "wrong",
            NewPassword = "newpassword",
            ConfirmNewPassword = "newpassword",
        };

        vm.Submit();

        vm.ErrorMessage.Should().Be("Incorrect password.");
        vm.IsSuccess.Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 2 — Wrong current PIN shows PIN error
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_WrongCurrentPin_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("pin");
        svc.VerifyMasterPassword("000000").Returns(false);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "000000",
            NewPassword = "123456",
            ConfirmNewPassword = "123456",
        };

        vm.Submit();

        vm.ErrorMessage.Should().Be("Incorrect PIN.");
        vm.IsSuccess.Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 3 — New password too short shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_PasswordTooShort_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("password");
        svc.VerifyMasterPassword("oldpassword").Returns(true);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "oldpassword",
            NewPassword = "short",
            ConfirmNewPassword = "short",
        };

        vm.Submit();

        vm.ErrorMessage.Should().Be("Password must be at least 8 characters.");
        vm.IsSuccess.Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 4 — PIN not six digits shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_PinNotSixDigits_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("pin");
        svc.VerifyMasterPassword("000000").Returns(true);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "000000",
            NewPassword = "12345",
            ConfirmNewPassword = "12345",
        };

        vm.Submit();

        vm.ErrorMessage.Should().Be("PIN must be exactly 6 digits.");
        vm.IsSuccess.Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 5 — Mismatch shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_Mismatch_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("password");
        svc.VerifyMasterPassword("oldpassword").Returns(true);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "oldpassword",
            NewPassword = "newpassword1",
            ConfirmNewPassword = "newpassword2",
        };

        vm.Submit();

        vm.ErrorMessage.Should().Be("Passwords do not match.");
        vm.IsSuccess.Should().BeFalse();
    }

    // --------------------------------------------------------------------
    // Test 6 — Success: calls Set, sets IsSuccess, scrubs fields
    // --------------------------------------------------------------------
    [Fact]
    public void Submit_Success_CallsSet_SetsIsSuccess_ScrubsFields()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("password");
        svc.VerifyMasterPassword("oldpassword").Returns(true);
        var vm = new ChangePasswordViewModel(svc)
        {
            CurrentPassword = "oldpassword",
            NewPassword = "newpassword",
            ConfirmNewPassword = "newpassword",
        };

        vm.Submit();

        svc.Received(1).SetMasterPassword("newpassword", "password");
        vm.IsSuccess.Should().BeTrue();
        vm.CurrentPassword.Should().BeEmpty();
        vm.NewPassword.Should().BeEmpty();
        vm.ConfirmNewPassword.Should().BeEmpty();
        vm.ErrorMessage.Should().BeNull();
    }

    // --------------------------------------------------------------------
    // Test 7 — Title in password mode
    // --------------------------------------------------------------------
    [Fact]
    public void Title_PasswordMode_ReturnsChangePassword()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("password");
        var vm = new ChangePasswordViewModel(svc);

        vm.Title.Should().Be("Change Password");
    }

    // --------------------------------------------------------------------
    // Test 8 — Title in PIN mode
    // --------------------------------------------------------------------
    [Fact]
    public void Title_PinMode_ReturnsChangePin()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.GetAuthMode().Returns("pin");
        var vm = new ChangePasswordViewModel(svc);

        vm.Title.Should().Be("Change PIN");
    }
}
