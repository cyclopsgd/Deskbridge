using Deskbridge.Core.Interfaces;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01 / SEC-02 / UI-SPEC §Lock Overlay Copywriting)
/// behavioural tests for <see cref="LockOverlayViewModel"/>. Validates both
/// first-run setup + unlock flows including validation error copy, event
/// dispatch, and post-success field scrub (T-06-05).
/// </summary>
public sealed class LockOverlayViewModelTests
{
    // --------------------------------------------------------------------
    // Test 1 — First-run: BodyCopy + ButtonCopy match UI-SPEC verbatim
    // --------------------------------------------------------------------
    [Fact]
    public void IsFirstRun_True_HasSetupCopy()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);

        vm.IsFirstRun.Should().BeTrue();
        vm.BodyCopy.Should().Be("Set a master password to protect your connections. This password cannot be recovered — choose something memorable.");
        vm.ButtonCopy.Should().Be("Set Password");
        vm.PasswordPlaceholder.Should().Be("New master password");
    }

    // --------------------------------------------------------------------
    // Test 2 — Unlock mode copy
    // --------------------------------------------------------------------
    [Fact]
    public void IsFirstRun_False_HasUnlockCopy()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(true);
        var vm = new LockOverlayViewModel(svc);

        vm.IsFirstRun.Should().BeFalse();
        vm.BodyCopy.Should().Be("Locked. Enter your master password to continue.");
        vm.ButtonCopy.Should().Be("Unlock");
        vm.PasswordPlaceholder.Should().Be("Master password");
    }

    // --------------------------------------------------------------------
    // Test 3 — Unlock success: VerifyMasterPassword true → UnlockSucceeded, Password cleared
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_CorrectPassword_RaisesUnlockSucceeded_AndClearsPassword()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(true);
        svc.VerifyMasterPassword("hunter2").Returns(true);
        var vm = new LockOverlayViewModel(svc);
        vm.Password = "hunter2";

        var fired = 0;
        vm.UnlockSucceeded += (_, _) => fired++;

        vm.UnlockCommand.Execute(null);

        fired.Should().Be(1);
        vm.ErrorMessage.Should().BeNull();
        vm.Password.Should().BeEmpty("T-06-05: password scrubbed after verify");
    }

    // --------------------------------------------------------------------
    // Test 4 — Unlock wrong password: error message, password cleared, RequestFocusPassword
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_WrongPassword_SetsError_ClearsPassword_RequestsFocus()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(true);
        svc.VerifyMasterPassword("wrong").Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.Password = "wrong";

        var focusRequested = 0;
        var unlockSucceeded = 0;
        vm.RequestFocusPassword += (_, _) => focusRequested++;
        vm.UnlockSucceeded += (_, _) => unlockSucceeded++;

        vm.UnlockCommand.Execute(null);

        vm.ErrorMessage.Should().Be("Incorrect password. Try again.");
        vm.Password.Should().BeEmpty();
        focusRequested.Should().Be(1);
        unlockSucceeded.Should().Be(0);
    }

    // --------------------------------------------------------------------
    // Test 5 — First-run too short: error, SetMasterPassword NOT called
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_TooShortPassword_ShowsError_DoesNotCallSet()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.Password = "short";
        vm.ConfirmPassword = "short";

        var unlockSucceeded = 0;
        vm.UnlockSucceeded += (_, _) => unlockSucceeded++;

        vm.UnlockCommand.Execute(null);

        vm.ErrorMessage.Should().Be("Password must be at least 8 characters.");
        svc.DidNotReceiveWithAnyArgs().SetMasterPassword(default!, default!);
        unlockSucceeded.Should().Be(0);
    }

    // --------------------------------------------------------------------
    // Test 6 — First-run mismatch: error, SetMasterPassword NOT called
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_MismatchedPasswords_ShowsError_DoesNotCallSet()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.Password = "longpassword";
        vm.ConfirmPassword = "differentpassword";

        vm.UnlockCommand.Execute(null);

        vm.ErrorMessage.Should().Be("Passwords do not match.");
        svc.DidNotReceiveWithAnyArgs().SetMasterPassword(default!, default!);
    }

    // --------------------------------------------------------------------
    // Test 7 — First-run success: SetMasterPassword called + UnlockSucceeded raised + scrub
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_ValidAndMatching_Success_CallsSet_RaisesEvent_ScrubsBoth()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.Password = "goodpassword";
        vm.ConfirmPassword = "goodpassword";

        var fired = 0;
        vm.UnlockSucceeded += (_, _) => fired++;

        vm.UnlockCommand.Execute(null);

        svc.Received(1).SetMasterPassword("goodpassword", "password");
        fired.Should().Be(1);
        vm.ErrorMessage.Should().BeNull();
        vm.Password.Should().BeEmpty("T-06-05: Password scrubbed after Set");
        vm.ConfirmPassword.Should().BeEmpty("T-06-05: ConfirmPassword scrubbed after Set");
    }

    // --------------------------------------------------------------------
    // Test 9 — First-run PIN mode: BodyCopy + ButtonCopy + Placeholder match PIN copy
    // --------------------------------------------------------------------
    [Fact]
    public void IsFirstRun_True_PinMode_HasPinCopy()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.IsPinMode = true;

        vm.BodyCopy.Should().Contain("6-digit PIN");
        vm.ButtonCopy.Should().Be("Set PIN");
        vm.PasswordPlaceholder.Should().Be("6-digit PIN");
    }

    // --------------------------------------------------------------------
    // Test 10 — First-run PIN mode: too few digits shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_PinMode_TooFewDigits_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.IsPinMode = true;
        vm.Password = "123";
        vm.ConfirmPassword = "123";

        vm.UnlockCommand.Execute(null);

        vm.ErrorMessage.Should().Be("PIN must be exactly 6 digits.");
    }

    // --------------------------------------------------------------------
    // Test 11 — First-run PIN mode: non-digits shows error
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_PinMode_NonDigits_ShowsError()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.IsPinMode = true;
        vm.Password = "abcdef";
        vm.ConfirmPassword = "abcdef";

        vm.UnlockCommand.Execute(null);

        vm.ErrorMessage.Should().Be("PIN must be exactly 6 digits.");
    }

    // --------------------------------------------------------------------
    // Test 12 — First-run PIN mode: valid PIN calls SetMasterPassword with "pin"
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_FirstRun_PinMode_ValidPin_CallsSetWithPinMode()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(false);
        var vm = new LockOverlayViewModel(svc);
        vm.IsPinMode = true;
        vm.Password = "123456";
        vm.ConfirmPassword = "123456";

        var fired = 0;
        vm.UnlockSucceeded += (_, _) => fired++;

        vm.UnlockCommand.Execute(null);

        svc.Received(1).SetMasterPassword("123456", "pin");
        fired.Should().Be(1);
        vm.Password.Should().BeEmpty();
        vm.ConfirmPassword.Should().BeEmpty();
    }

    // --------------------------------------------------------------------
    // Test 13 — Unlock mode: PIN mode read from service
    // --------------------------------------------------------------------
    [Fact]
    public void Unlock_UnlockMode_PinMode_ReadFromService()
    {
        var svc = Substitute.For<IMasterPasswordService>();
        svc.IsMasterPasswordSet().Returns(true);
        svc.GetAuthMode().Returns("pin");
        var vm = new LockOverlayViewModel(svc);

        vm.IsPinMode.Should().BeTrue();
        vm.IsPasswordMode.Should().BeFalse();
        vm.PasswordPlaceholder.Should().Be("PIN");
    }

    // --------------------------------------------------------------------
    // Test 8 — Source-grep (Pitfall 8): the dialog code-behind must contain the
    //          Enter-in-PasswordBox guard. KeyEventArgs has internal-only ctors
    //          so direct invocation would need reflection into WPF internals.
    //          Pattern matches DiCompositionTests.CommandPaletteDialog_Has_Pitfall8_EnterHandler.
    // --------------------------------------------------------------------
    [Fact]
    public void LockOverlayDialog_HasPitfall8EnterHandler()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var dlgCs = System.IO.File.ReadAllText(System.IO.Path.Combine(solutionRoot,
            "src", "Deskbridge", "Dialogs", "LockOverlayDialog.xaml.cs"));

        dlgCs.Should().Contain("Dialog_PreviewKeyDown");
        dlgCs.Should().Contain("Key.Enter");
        dlgCs.Should().Contain("PasswordBox");
        dlgCs.Should().Contain("UnlockCommand");
        dlgCs.Should().Contain("e.Handled = true");

        // And the XAML declares IsFooterVisible="False" + opaque ContentDialogSmokeFill override
        var dlgXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(solutionRoot,
            "src", "Deskbridge", "Dialogs", "LockOverlayDialog.xaml"));

        dlgXaml.Should().Contain("IsFooterVisible=\"False\"");
        dlgXaml.Should().Contain("ContentDialogSmokeFill",
            "SEC-02 full-window opaque override per user checkpoint feedback — SmokeGrid " +
            "Fill is bound to DynamicResource ContentDialogSmokeFill; overriding the brush " +
            "key at dialog scope makes the backdrop opaque and prevents shell bleed-through");
    }

    private static string FindSolutionRoot(string startPath)
    {
        var dir = new System.IO.DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (dir.GetFiles("Deskbridge.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate Deskbridge.sln from {startPath}");
    }
}
