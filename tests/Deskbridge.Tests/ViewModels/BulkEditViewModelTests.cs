using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using FluentAssertions;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// BULK-03 — BulkEditViewModel field-diff / per-field-enable / ApplyToModels / validation
/// behavior contract (Phase 23 — bulk-operations-ux).
///
/// Wave 0 (23-01) pinned these as skipped placeholders. Plan 23-02 implements the
/// dependency-light <see cref="BulkEditViewModel"/> and makes every test below real and green.
///
/// Editable field set (VERIFIED in 23-PATTERNS.md): Hostname, Port, CredentialMode, Username,
/// Domain, GroupId. Name is EXCLUDED. Password is never in scope.
/// </summary>
public class BulkEditViewModelTests
{
    /// <summary>
    /// Canonical scaffold fixture: 2 models with one SHARED field (Domain = "CORP") and one
    /// DIVERGENT field (Hostname: alpha vs bravo; Port also diverges 3389 vs 3390).
    /// </summary>
    private static List<ConnectionModel> BuildDivergentModels() =>
    [
        new ConnectionModel { Id = Guid.NewGuid(), Name = "A", Hostname = "alpha.local",  Domain = "CORP", Port = 3389 },
        new ConnectionModel { Id = Guid.NewGuid(), Name = "B", Hostname = "bravo.local",  Domain = "CORP", Port = 3390 },
    ];

    [Fact]
    public void SharedField_PreFillsSharedValue_CheckboxUnchecked()
    {
        var models = BuildDivergentModels(); // both share Domain = "CORP"
        var sut = new BulkEditViewModel(models);

        sut.DomainField.IsShared.Should().BeTrue();
        sut.DomainField.Value.Should().Be("CORP");      // pre-filled shared value
        sut.DomainField.IsEnabled.Should().BeFalse();   // checkbox unchecked by default
        sut.DomainField.Placeholder.Should().BeEmpty(); // no "Multiple values" hint on shared
    }

    [Fact]
    public void DivergentField_IsSharedFalse_ShowsMultipleValuesPlaceholder()
    {
        var models = BuildDivergentModels(); // Hostname diverges (alpha vs bravo)
        var sut = new BulkEditViewModel(models);

        sut.HostnameField.IsShared.Should().BeFalse();
        sut.HostnameField.Placeholder.Should().Be("Multiple values");
        sut.HostnameField.Value.Should().BeNullOrEmpty(); // blank until enabled + edited
    }

    [Fact]
    public void CanApply_FalseUntilAtLeastOneFieldChecked_TrueAfter()
    {
        var sut = new BulkEditViewModel(BuildDivergentModels());

        sut.CanApply.Should().BeFalse();    // nothing checked
        sut.HostnameField.IsEnabled = true; // check one field
        sut.HostnameField.Value = "new.local";
        sut.CanApply.Should().BeTrue();
    }

    [Fact]
    public void ApplyToModels_WritesOnlyCheckedFields_LeavesUncheckedDivergentUntouched()
    {
        var models = BuildDivergentModels(); // Hostname divergent, Domain shared
        var sut = new BulkEditViewModel(models);

        sut.DomainField.IsEnabled = true;
        sut.DomainField.Value = "NEWDOM"; // checked
        // Hostname left UNCHECKED → must remain per-model divergent

        sut.ApplyToModels(models);

        models.Should().AllSatisfy(m => m.Domain.Should().Be("NEWDOM"));
        models[0].Hostname.Should().Be("alpha.local"); // unchecked divergent untouched
        models[1].Hostname.Should().Be("bravo.local");
    }

    [Fact]
    public void ApplyToModels_NeverModifiesName()
    {
        var models = BuildDivergentModels();
        var sut = new BulkEditViewModel(models);

        // There is NO Name field on the VM (Name excluded from the editable set).
        sut.HostnameField.IsEnabled = true;
        sut.HostnameField.Value = "x.local";
        sut.ApplyToModels(models);

        models[0].Name.Should().Be("A");
        models[1].Name.Should().Be("B");
    }

    [Fact]
    public void ApplyToModels_NeverModifiesPassword()
    {
        // Password is not part of ConnectionModel (stored via ICredentialService). The VM exposes
        // NO password field and ApplyToModels never touches credential storage — verified here by
        // the absence of any credential write path: applying Username does not surface a password.
        var models = BuildDivergentModels();
        var sut = new BulkEditViewModel(models);

        sut.UsernameField.IsEnabled = true;
        sut.UsernameField.Value = "svc";
        var result = sut.ApplyToModels(models);

        result.Should().AllSatisfy(m => m.Username.Should().Be("svc"));
        // ConnectionModel has no password property; bulk edit is dependency-light and injects no
        // ICredentialService, so no secret can be read or written by construction.
        typeof(ConnectionModel).GetProperties()
            .Should().NotContain(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        typeof(BulkEditViewModel).GetProperties()
            .Should().NotContain(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsPortOutOf1To65535_WhenPortEnabled()
    {
        var sut = new BulkEditViewModel(BuildDivergentModels());
        sut.PortField.IsEnabled = true;

        sut.PortField.Value = "70000"; // out of 1..65535
        sut.Validate().Should().BeFalse();

        sut.PortField.Value = "0"; // below range
        sut.Validate().Should().BeFalse();

        sut.PortField.Value = "65535"; // boundary OK
        sut.Validate().Should().BeTrue();

        sut.PortField.Value = "1"; // boundary OK
        sut.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsEmptyHostname_WhenHostnameEnabled()
    {
        var sut = new BulkEditViewModel(BuildDivergentModels());
        sut.HostnameField.IsEnabled = true;

        sut.HostnameField.Value = "";   // empty not allowed when checked
        sut.Validate().Should().BeFalse();

        sut.HostnameField.Value = "   "; // whitespace not allowed
        sut.Validate().Should().BeFalse();

        sut.HostnameField.Value = "ok.local";
        sut.Validate().Should().BeTrue();
    }

    [Fact]
    public void ApplyToModels_GroupField_SetsGroupIdOnConnections()
    {
        var targetGroupId = Guid.NewGuid();
        var models = BuildDivergentModels();
        var sut = new BulkEditViewModel(models);

        sut.GroupField.IsEnabled = true;
        sut.GroupField.Value = targetGroupId; // GroupId is Guid? on ConnectionModel
        sut.ApplyToModels(models);

        models.Should().AllSatisfy(m => m.GroupId.Should().Be(targetGroupId));
    }
}
