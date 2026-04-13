using System.Net;
using AdysTech.CredentialManager;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;
using CredentialType = AdysTech.CredentialManager.CredentialType;

namespace Deskbridge.Core.Services;

public sealed class WindowsCredentialService : ICredentialService
{
    // Windows Credential Manager reserves the TERMSRV/* target prefix for
    // CRED_TYPE_DOMAIN_PASSWORD (the RDP SSO convention used by mstsc). The
    // AdysTech wrapper exposes this as CredentialType.Windows (underlying value 2,
    // which is CRED_TYPE_DOMAIN_PASSWORD in Wincred.h). Writing Generic to a fresh
    // TERMSRV/* target fails with 0x8 (ERROR_NOT_ENOUGH_MEMORY) because Windows
    // masks the type-conflict behind that misleading code.
    private const CredentialType RdpTargetType = CredentialType.Windows;

    // Older installs or cmdkey /generic invocations may have left Generic-type
    // entries under TERMSRV/*. Read path falls back to this so existing users
    // don't lose saved credentials after the fix.
    private const CredentialType LegacyRdpTargetType = CredentialType.Generic;

    public NetworkCredential? GetForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        try
        {
            return CredentialManager.GetCredentials(target, RdpTargetType)
                ?? CredentialManager.GetCredentials(target, LegacyRdpTargetType);
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
        try
        {
            var cred = new NetworkCredential(username, password, domain ?? string.Empty);
            CredentialManager.SaveCredentials(target, cred, RdpTargetType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store credentials for target {Target}", target);
            throw;
        }
    }

    public void DeleteForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        try
        {
            CredentialManager.RemoveCredentials(target, RdpTargetType);
        }
        catch
        {
            // Credential may not exist -- swallow
        }
        try
        {
            CredentialManager.RemoveCredentials(target, LegacyRdpTargetType);
        }
        catch
        {
            // Legacy Generic entry may not exist -- swallow
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
        try
        {
            var cred = new NetworkCredential(username, password, domain ?? string.Empty);
            CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store group credentials for target {Target}", target);
            throw;
        }
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
        // Walk the group parent chain, guarding against cycles (bad data can produce
        // infinite loops otherwise: group A.Parent=B, B.Parent=A).
        var visited = new HashSet<Guid>();
        var groupId = connection.GroupId;
        while (groupId.HasValue)
        {
            if (!visited.Add(groupId.Value))
            {
                Log.Warning("Cycle detected in group parent chain starting at {GroupId}", groupId.Value);
                break;
            }

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
