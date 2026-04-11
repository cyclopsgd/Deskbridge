using System.Net;
using AdysTech.CredentialManager;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;
using CredentialType = AdysTech.CredentialManager.CredentialType;

namespace Deskbridge.Core.Services;

public sealed class WindowsCredentialService : ICredentialService
{
    public NetworkCredential? GetForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        try
        {
            return CredentialManager.GetCredentials(target, CredentialType.Generic);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retrieve credentials for target {Target}", target);
            return null;
        }
    }

    public void StoreForConnection(ConnectionModel connection, string username, string? domain, string password)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        var cred = new NetworkCredential(username, password, domain ?? string.Empty);
        CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
    }

    public void DeleteForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        try
        {
            CredentialManager.RemoveCredentials(target, CredentialType.Generic);
        }
        catch
        {
            // Credential may not exist -- swallow
        }
    }

    public NetworkCredential? GetForGroup(Guid groupId)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        try
        {
            return CredentialManager.GetCredentials(target, CredentialType.Generic);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retrieve group credentials for target {Target}", target);
            return null;
        }
    }

    public void StoreForGroup(Guid groupId, string username, string? domain, string password)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        var cred = new NetworkCredential(username, password, domain ?? string.Empty);
        CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
    }

    public void DeleteForGroup(Guid groupId)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        try
        {
            CredentialManager.RemoveCredentials(target, CredentialType.Generic);
        }
        catch
        {
            // Credential may not exist -- swallow
        }
    }

    public NetworkCredential? ResolveInherited(ConnectionModel connection, IConnectionStore connectionStore)
    {
        var groupId = connection.GroupId;
        while (groupId.HasValue)
        {
            var cred = GetForGroup(groupId.Value);
            if (cred is not null)
                return cred;

            var group = connectionStore.GetGroupById(groupId.Value);
            groupId = group?.ParentGroupId;
        }
        return null;
    }

    public bool HasGroupCredentials(Guid groupId) => GetForGroup(groupId) is not null;
}
