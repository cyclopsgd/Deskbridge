using Deskbridge.Core.Models;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// BULK-03 — BulkEditViewModel field-diff / per-field-enable / ApplyToModels / validation
/// behavior contract (Phase 23 — bulk-operations-ux).
///
/// WAVE 0 SCAFFOLD: BulkEditViewModel does not exist yet — it is implemented by plan 23-02.
/// Every test below is [Fact(Skip = ...)] and pins ONE row of the 23-VALIDATION.md Per-Task
/// Verification Map (BULK-03 VM rows). Each body documents the intended Arrange/Act/Assert in a
/// `// TODO 23-02:` block plus a trivial Assert so the file COMPILES against existing types today.
///
/// Editable field set (VERIFIED in 23-PATTERNS.md): Hostname, Port, CredentialMode, Username,
/// Domain, GroupId. Name is EXCLUDED. Password is never in scope.
///
/// Plan 23-02 makes these green by:
///   1. Adding the BulkEditViewModel production type (thin, dependency-light — takes the selected
///      ConnectionModels per the planner note in 23-VALIDATION.md).
///   2. Replacing each `// TODO 23-02:` block with the real sut + Act/Assert and removing Skip.
/// </summary>
public class BulkEditViewModelTests
{
    /// <summary>
    /// Helper documenting the canonical scaffold fixture: 2+ models with one SHARED field
    /// (Domain) and one DIVERGENT field (Hostname). Plans 23-02 will feed these into the VM ctor.
    /// </summary>
    private static List<ConnectionModel> BuildDivergentModels() =>
    [
        new ConnectionModel { Id = Guid.NewGuid(), Name = "A", Hostname = "alpha.local",  Domain = "CORP", Port = 3389 },
        new ConnectionModel { Id = Guid.NewGuid(), Name = "B", Hostname = "bravo.local",  Domain = "CORP", Port = 3390 },
    ];

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void SharedField_PreFillsSharedValue_CheckboxUnchecked()
    {
        // TODO 23-02:
        //   var models = BuildDivergentModels(); // both share Domain = "CORP"
        //   var sut = new BulkEditViewModel(models);
        //   sut.DomainField.IsShared.Should().BeTrue();
        //   sut.DomainField.Value.Should().Be("CORP");        // pre-filled shared value
        //   sut.DomainField.IsEnabled.Should().BeFalse();      // checkbox unchecked by default
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void DivergentField_IsSharedFalse_ShowsMultipleValuesPlaceholder()
    {
        // TODO 23-02:
        //   var models = BuildDivergentModels(); // Hostname diverges (alpha vs bravo)
        //   var sut = new BulkEditViewModel(models);
        //   sut.HostnameField.IsShared.Should().BeFalse();
        //   sut.HostnameField.Placeholder.Should().Be("Multiple values");
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void CanApply_FalseUntilAtLeastOneFieldChecked_TrueAfter()
    {
        // TODO 23-02:
        //   var sut = new BulkEditViewModel(BuildDivergentModels());
        //   sut.CanApply.Should().BeFalse();                  // nothing checked
        //   sut.HostnameField.IsEnabled = true;               // check one field
        //   sut.HostnameField.Value = "new.local";
        //   sut.CanApply.Should().BeTrue();
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void ApplyToModels_WritesOnlyCheckedFields_LeavesUncheckedDivergentUntouched()
    {
        // TODO 23-02:
        //   var models = BuildDivergentModels(); // Hostname divergent, Domain shared
        //   var sut = new BulkEditViewModel(models);
        //   sut.DomainField.IsEnabled = true;  sut.DomainField.Value = "NEWDOM";  // checked
        //   // Hostname left UNCHECKED → must remain per-model divergent
        //   sut.ApplyToModels(models);
        //   models.Should().AllSatisfy(m => m.Domain.Should().Be("NEWDOM"));
        //   models[0].Hostname.Should().Be("alpha.local");   // unchecked divergent untouched
        //   models[1].Hostname.Should().Be("bravo.local");
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void ApplyToModels_NeverModifiesName()
    {
        // TODO 23-02:
        //   var models = BuildDivergentModels();
        //   var sut = new BulkEditViewModel(models);
        //   // There must be NO Name field on the VM at all (Name excluded from editable set).
        //   sut.HostnameField.IsEnabled = true; sut.HostnameField.Value = "x.local";
        //   sut.ApplyToModels(models);
        //   models[0].Name.Should().Be("A");
        //   models[1].Name.Should().Be("B");
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void ApplyToModels_NeverModifiesPassword()
    {
        // TODO 23-02:
        //   Password is not part of ConnectionModel (stored via ICredentialService) — the VM must
        //   expose NO password field and ApplyToModels must never touch credential storage.
        //   var models = BuildDivergentModels();
        //   var sut = new BulkEditViewModel(models);
        //   sut.UsernameField.IsEnabled = true; sut.UsernameField.Value = "svc";
        //   sut.ApplyToModels(models);
        //   // Assert: no credential write occurred (covered by credential-service substitute in 23-02).
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void Validate_RejectsPortOutOf1To65535_WhenPortEnabled()
    {
        // TODO 23-02:
        //   var sut = new BulkEditViewModel(BuildDivergentModels());
        //   sut.PortField.IsEnabled = true;
        //   sut.PortField.Value = "70000";  // out of 1..65535
        //   sut.Validate().Should().BeFalse();
        //   sut.PortField.Value = "65535";  // boundary OK
        //   sut.Validate().Should().BeTrue();
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void Validate_RejectsEmptyHostname_WhenHostnameEnabled()
    {
        // TODO 23-02:
        //   var sut = new BulkEditViewModel(BuildDivergentModels());
        //   sut.HostnameField.IsEnabled = true;
        //   sut.HostnameField.Value = "";    // empty not allowed when checked
        //   sut.Validate().Should().BeFalse();
        //   sut.HostnameField.Value = "ok.local";
        //   sut.Validate().Should().BeTrue();
        Assert.True(true);
    }

    [Fact(Skip = "Wave 1: implemented by 23-02 (BulkEditViewModel)")]
    public void ApplyToModels_GroupField_SetsGroupIdOnConnections()
    {
        // TODO 23-02:
        //   var targetGroupId = Guid.NewGuid();
        //   var models = BuildDivergentModels();
        //   var sut = new BulkEditViewModel(models);
        //   sut.GroupField.IsEnabled = true;
        //   sut.GroupField.Value = targetGroupId;   // GroupId is Guid? on ConnectionModel
        //   sut.ApplyToModels(models);
        //   models.Should().AllSatisfy(m => m.GroupId.Should().Be(targetGroupId));
        Assert.True(true);
    }
}
