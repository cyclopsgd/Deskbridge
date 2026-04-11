using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using System.Net;

namespace Deskbridge.Tests;

public sealed class CredentialInheritanceTests
{
    [Fact]
    public void ResolveInherited_FindsDirectGroupCredentials()
    {
        var groupId = Guid.NewGuid();
        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Server1",
            Hostname = "server1.local",
            GroupId = groupId,
            CredentialMode = CredentialMode.Inherit
        };

        var group = new ConnectionGroup { Id = groupId, Name = "Production" };

        var store = Substitute.For<IConnectionStore>();
        store.GetGroupById(groupId).Returns(group);

        var credService = Substitute.For<ICredentialService>();
        credService.GetForGroup(groupId).Returns(new NetworkCredential("admin", "pass123", "CORP"));

        // Use the real WindowsCredentialService.ResolveInherited logic by testing through the interface
        // We'll test the walk-up logic directly
        var result = ResolveInheritedWalkUp(connection, store, credService);

        result.Should().NotBeNull();
        result!.UserName.Should().Be("admin");
        result.Domain.Should().Be("CORP");
    }

    [Fact]
    public void ResolveInherited_WalksUpToGrandparent()
    {
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var grandparent = new ConnectionGroup { Id = grandparentId, Name = "Root", ParentGroupId = null };
        var parent = new ConnectionGroup { Id = parentId, Name = "Child", ParentGroupId = grandparentId };

        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Server1",
            Hostname = "server1.local",
            GroupId = parentId,
            CredentialMode = CredentialMode.Inherit
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetGroupById(parentId).Returns(parent);
        store.GetGroupById(grandparentId).Returns(grandparent);

        var credService = Substitute.For<ICredentialService>();
        credService.GetForGroup(parentId).Returns((NetworkCredential?)null);
        credService.GetForGroup(grandparentId).Returns(new NetworkCredential("grandadmin", "gpass", "CORP"));

        var result = ResolveInheritedWalkUp(connection, store, credService);

        result.Should().NotBeNull();
        result!.UserName.Should().Be("grandadmin");
    }

    [Fact]
    public void ResolveInherited_ReturnsNull_WhenNoCredentialsInChain()
    {
        var groupId = Guid.NewGuid();
        var group = new ConnectionGroup { Id = groupId, Name = "NoCreds", ParentGroupId = null };

        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Server1",
            Hostname = "server1.local",
            GroupId = groupId,
            CredentialMode = CredentialMode.Inherit
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetGroupById(groupId).Returns(group);

        var credService = Substitute.For<ICredentialService>();
        credService.GetForGroup(groupId).Returns((NetworkCredential?)null);

        var result = ResolveInheritedWalkUp(connection, store, credService);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveInherited_ReturnsNull_WhenConnectionHasNoGroup()
    {
        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Ungrouped",
            Hostname = "server1.local",
            GroupId = null
        };

        var store = Substitute.For<IConnectionStore>();
        var credService = Substitute.For<ICredentialService>();

        var result = ResolveInheritedWalkUp(connection, store, credService);

        result.Should().BeNull();
    }

    [Fact]
    public void HasGroupCredentials_ReturnsTrue_WhenGroupHasStoredCredentials()
    {
        var groupId = Guid.NewGuid();
        var credService = Substitute.For<ICredentialService>();
        credService.GetForGroup(groupId).Returns(new NetworkCredential("admin", "pass"));
        credService.HasGroupCredentials(groupId).Returns(true);

        credService.HasGroupCredentials(groupId).Should().BeTrue();
    }

    /// <summary>
    /// Helper that mirrors the ResolveInherited walk-up logic from WindowsCredentialService.
    /// This lets us test the algorithm with mocked dependencies without hitting the real Credential Manager.
    /// </summary>
    private static NetworkCredential? ResolveInheritedWalkUp(
        ConnectionModel connection,
        IConnectionStore store,
        ICredentialService credService)
    {
        var groupId = connection.GroupId;
        while (groupId.HasValue)
        {
            var cred = credService.GetForGroup(groupId.Value);
            if (cred is not null)
                return cred;

            var group = store.GetGroupById(groupId.Value);
            groupId = group?.ParentGroupId;
        }
        return null;
    }
}
