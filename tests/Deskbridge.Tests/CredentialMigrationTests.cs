using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class CredentialMigrationTests
{
    [Fact]
    public void BuildConnectionTarget_ReturnsDesKbridgeConnFormat()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var target = WindowsCredentialService.BuildConnectionTarget(id);
        target.Should().Be("DESKBRIDGE/CONN/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void BuildConnectionTarget_UsesConnectionId_NotHostname()
    {
        var id = Guid.NewGuid();
        var target = WindowsCredentialService.BuildConnectionTarget(id);
        target.Should().StartWith("DESKBRIDGE/CONN/");
        target.Should().EndWith(id.ToString());
    }

    [Fact]
    public void BuildLegacyTarget_ReturnsTermsrvFormat()
    {
        var target = WindowsCredentialService.BuildLegacyTarget("server1.local");
        target.Should().Be("TERMSRV/server1.local");
    }

    [Fact]
    public void MigrateFromTermsrv_AcceptsIConnectionStore()
    {
        // Compile-time check: MigrateFromTermsrv exists with correct signature
        var service = new WindowsCredentialService();
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(Array.Empty<ConnectionModel>());

        // Should not throw on empty connection list
        service.MigrateFromTermsrv(store);
    }

    [Fact]
    public void MigrateFromTermsrv_HandlesEmptyConnectionStore()
    {
        var service = new WindowsCredentialService();
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(Array.Empty<ConnectionModel>());

        var ex = Record.Exception(() => service.MigrateFromTermsrv(store));
        ex.Should().BeNull();
    }
}
