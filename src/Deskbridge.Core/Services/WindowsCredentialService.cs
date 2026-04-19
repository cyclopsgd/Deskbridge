using System.Net;
using AdysTech.CredentialManager;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;
using CredentialType = AdysTech.CredentialManager.CredentialType;

namespace Deskbridge.Core.Services;

public sealed class WindowsCredentialService : ICredentialService
{
    // Connection credentials use DESKBRIDGE/CONN/{connectionId} as the Windows
    // Credential Manager target. This avoids the TERMSRV/* prefix which Windows
    // auto-discovers for RDP CredSSP delegation -- on machines with Credential
    // Guard enabled, TERMSRV/* entries trigger "Windows Defender Credential Guard
    // does not allow using saved credentials" even though Deskbridge injects the
    // password directly via IMsTscNonScriptable.ClearTextPassword. The DESKBRIDGE/*
    // namespace is invisible to CredSSP negotiation, eliminating the conflict.
    //
    // CredentialType.Generic is used for all DESKBRIDGE/* targets (both connection
    // and group). This is consistent and avoids the Windows-reserved
    // CRED_TYPE_DOMAIN_PASSWORD (CredentialType.Windows) that TERMSRV/* required.

    public NetworkCredential? GetForConnection(ConnectionModel connection)
    {
        var target = BuildConnectionTarget(connection.Id);
        try
        {
            return NormalizeCredential(CredentialManager.GetCredentials(target, CredentialType.Generic));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retrieve credentials for target {Target}", target);
            return null;
        }
    }

    public void StoreForConnection(ConnectionModel connection, string username, string? domain, string password)
    {
        var target = BuildConnectionTarget(connection.Id);
        try
        {
            domain = NormalizeDomain(domain);
            username = StripDomainPrefix(username, domain);
            var cred = new NetworkCredential(username, password, domain ?? string.Empty);
            CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store credentials for target {Target}", target);
            throw;
        }
    }

    public void DeleteForConnection(ConnectionModel connection)
    {
        var target = BuildConnectionTarget(connection.Id);
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
            return NormalizeCredential(CredentialManager.GetCredentials(target, CredentialType.Generic));
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
            domain = NormalizeDomain(domain);
            username = StripDomainPrefix(username, domain);
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

    /// <summary>
    /// One-time migration: moves credentials from legacy TERMSRV/{hostname} targets
    /// to DESKBRIDGE/CONN/{connectionId} targets. Idempotent -- skips connections
    /// that already have credentials under the new target. Called once at startup.
    /// </summary>
    public void MigrateFromTermsrv(IConnectionStore connectionStore)
    {
        foreach (var connection in connectionStore.GetAll())
        {
            try
            {
                // Skip if new target already has credentials
                var newTarget = BuildConnectionTarget(connection.Id);
                if (CredentialManager.GetCredentials(newTarget, CredentialType.Generic) is not null)
                    continue;

                // Try to read from old TERMSRV/{hostname} target.
                // Try DomainPassword first (canonical for TERMSRV/*), then Generic (legacy).
                var oldTarget = BuildLegacyTarget(connection.Hostname);
                var oldCred = CredentialManager.GetCredentials(oldTarget, CredentialType.Windows)
                           ?? CredentialManager.GetCredentials(oldTarget, CredentialType.Generic);

                if (oldCred is null)
                    continue;

                // Write to new target
                CredentialManager.SaveCredentials(newTarget, oldCred, CredentialType.Generic);
                Log.Information("Migrated credentials for {ConnectionName} from {OldTarget} to {NewTarget}",
                    connection.Name, oldTarget, newTarget);

                // Clean up old entries (both types, swallow failures)
                try { CredentialManager.RemoveCredentials(oldTarget, CredentialType.Windows); } catch { }
                try { CredentialManager.RemoveCredentials(oldTarget, CredentialType.Generic); } catch { }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to migrate credentials for connection {ConnectionName} ({ConnectionId})",
                    connection.Name, connection.Id);
                // Continue with next connection -- partial migration is fine
            }
        }
    }

    /// <summary>
    /// Normalizes a <see cref="NetworkCredential"/> returned by Windows Credential Manager.
    /// CredMan internally stores domain\username as a single <c>UserName</c> field; on
    /// read-back, <c>Domain</c> is empty and <c>UserName</c> contains "DOMAIN\user".
    /// This method splits on the first backslash to restore separate fields.
    /// </summary>
    internal static NetworkCredential? NormalizeCredential(NetworkCredential? cred)
    {
        if (cred is null)
            return null;

        // If Domain is already populated, CredMan (or caller) split correctly -- leave as-is.
        if (!string.IsNullOrEmpty(cred.Domain))
            return cred;

        var username = cred.UserName;
        if (string.IsNullOrEmpty(username))
            return cred;

        var backslashIndex = username.IndexOf('\\');
        if (backslashIndex < 0)
            return cred;

        // Split on the FIRST backslash only: "DOMAIN\sub\user" -> domain="DOMAIN", user="sub\user"
        cred.Domain = username[..backslashIndex];
        cred.UserName = username[(backslashIndex + 1)..];
        return cred;
    }

    /// <summary>
    /// Strips trailing backslash from domain values. Users commonly type ".\\" meaning
    /// local machine, but the backslash is a separator convention, not part of the domain.
    /// Storing ".\\" as the domain causes CredentialManager to produce ".\\\\username"
    /// which NormalizeCredential then mis-splits.
    /// </summary>
    internal static string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return domain;

        return domain.TrimEnd('\\');
    }

    /// <summary>
    /// Defensive guard for Store operations: if the username already contains the
    /// domain prefix (from a previously corrupted read), strip it to prevent
    /// double-concatenation when CredMan re-merges domain\username internally.
    /// </summary>
    internal static string StripDomainPrefix(string username, string? domain)
    {
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(username))
            return username;

        var prefix = domain + "\\";
        if (username.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return username[prefix.Length..];

        return username;
    }

    /// <summary>
    /// Builds the Windows Credential Manager target for a connection.
    /// Format: DESKBRIDGE/CONN/{connectionId}
    /// </summary>
    internal static string BuildConnectionTarget(Guid connectionId) =>
        $"DESKBRIDGE/CONN/{connectionId}";

    /// <summary>
    /// Builds the legacy TERMSRV target used before the Credential Guard fix.
    /// Format: TERMSRV/{hostname}
    /// Used only by <see cref="MigrateFromTermsrv"/> to locate old entries.
    /// </summary>
    internal static string BuildLegacyTarget(string hostname) =>
        $"TERMSRV/{hostname}";
}
