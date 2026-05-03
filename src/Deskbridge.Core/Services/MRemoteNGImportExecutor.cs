using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 22 Plan 22-01 (D-01..D-04, D-06, D-07, D-13..D-15): the prepare-loop
/// service used by the import wizard VM. Pure logic — no DI dependencies, no
/// IConnectionStore (the VM owns the SaveBatch call per D-02).
///
/// Semantics ported verbatim from <c>ImportWizardViewModel.cs:300-361</c>
/// (Phase 7) — the four-branch dedup logic (Skip / Rename / Overwrite /
/// auto-rename) is preserved exactly. The structural change is failure
/// collection (per-row try/catch -> <see cref="FailedImport"/>) and progress
/// reporting via <see cref="IProgress{T}"/>.
/// </summary>
public class MRemoteNGImportExecutor : IImportExecutor
{
    public Task<ImportPrepareResult> PrepareAsync(
        ImportRequest request,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        // D-06: CT is honored at loop entry only.
        ct.ThrowIfCancellationRequested();

        // Build dedup index from existing connections (case-insensitive hostname
        // match — preserves VM:273 semantics).
        var existingHostnames = new HashSet<string>(
            request.ExistingConnections
                .Where(c => !string.IsNullOrEmpty(c.Hostname))
                .Select(c => c.Hostname!),
            StringComparer.OrdinalIgnoreCase);

        var connectionsToSave = new List<ConnectionModel>();
        var groupsToSave = new List<ConnectionGroup>();
        var failures = new List<FailedImport>();
        var groupMap = new Dictionary<string, Guid>();

        // Walk the checked-node tree, materializing groups eagerly and yielding
        // each leaf paired with its group id. Mirrors VM.FlattenCheckedItems
        // (VM:413-440) with the same parent-keyed group-reuse semantics.
        var flatItems = FlattenAndEnsureGroups(
            request.CheckedNodes,
            parentGroupId: null,
            groupMap,
            request.ExistingGroups,
            groupsToSave);

        int imported = 0, skipped = 0, renamed = 0;
        int processed = 0;

        foreach (var (node, groupId) in flatItems)
        {
            // Non-RDP rows are filtered BEFORE the row is "processed" — the user's
            // mental model is that progress denominator counts only the rows that
            // would land. This matches VM:296 (`IsSupported` continue before any
            // side effect). The progress denominator the VM passes (TotalToImport)
            // is the count of CONNECTION-typed RDP-protocol items only — see Plan
            // 22-02 for the matching CountConnections helper.
            if (node.Protocol != Protocol.Rdp)
                continue;

            string nodeNameForFailure = node.Name ?? string.Empty;
            try
            {
                // Defensive: reject rows whose Name is null. ImportedNode declares
                // Name as non-nullable, but XML-derived input can leak null through
                // null-forgiving casts. Catching it here keeps the rest of the
                // pipeline honest (D-07 continue-and-collect).
                if (node.Name is null)
                    throw new ArgumentException("Imported node has null Name attribute");

                var hostname = node.Hostname;

                // Look up the user's per-hostname resolution (if any).
                DuplicateResolution? resolution = null;
                bool isDuplicate = !string.IsNullOrEmpty(hostname)
                                   && existingHostnames.Contains(hostname);
                if (isDuplicate)
                {
                    resolution = request.Resolutions.FirstOrDefault(r =>
                        string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
                }

                if (isDuplicate)
                {
                    if (resolution is { Action: DuplicateAction.Skip })
                    {
                        skipped++;
                    }
                    else if (resolution is { Action: DuplicateAction.Rename })
                    {
                        // Append suffix to avoid conflict (VM:313-322).
                        var conn = CreateConnectionModel(node, groupId);
                        conn.Name = $"{conn.Name} (imported)";
                        connectionsToSave.Add(conn);
                        if (!string.IsNullOrEmpty(conn.Hostname))
                            existingHostnames.Add(conn.Hostname);
                        imported++;
                        renamed++;
                    }
                    else if (resolution is { Action: DuplicateAction.Overwrite })
                    {
                        // Find and update existing in place (VM:325-343).
                        var existing = request.ExistingConnections.FirstOrDefault(c =>
                            string.Equals(c.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            existing.Name = node.Name;
                            existing.Port = node.Port;
                            existing.Username = node.Username;
                            existing.Domain = node.Domain;
                            existing.Protocol = node.Protocol;
                            existing.GroupId = groupId;
                            existing.UpdatedAt = DateTime.UtcNow;
                            connectionsToSave.Add(existing);
                            imported++;
                        }
                    }
                    else
                    {
                        // Auto-rename branch when no explicit resolution
                        // (VM:346-352).
                        var autoConn = CreateConnectionModel(node, groupId);
                        autoConn.Name = $"{autoConn.Name} (imported)";
                        connectionsToSave.Add(autoConn);
                        if (!string.IsNullOrEmpty(autoConn.Hostname))
                            existingHostnames.Add(autoConn.Hostname);
                        imported++;
                        renamed++;
                    }
                }
                else
                {
                    // Straight import — no duplicate (VM:356-360).
                    var newConn = CreateConnectionModel(node, groupId);
                    connectionsToSave.Add(newConn);
                    if (!string.IsNullOrEmpty(newConn.Hostname))
                        existingHostnames.Add(newConn.Hostname);
                    imported++;
                }
            }
            catch (Exception ex)
            {
                // D-07 continue-and-collect: per-row failures appended; loop
                // continues. Fatal exceptions inside FlattenAndEnsureGroups
                // (above) propagate — those are invariant violations the VM
                // surface as ErrorMessage.
                failures.Add(new FailedImport(
                    ConnectionName: nodeNameForFailure,
                    Type: ImportFailureType.Unknown,
                    Detail: ex.Message));
            }
            finally
            {
                // D-03: failures still count toward the progress total.
                processed++;
                progress?.Report(processed);
            }
        }

        return Task.FromResult(new ImportPrepareResult(
            ConnectionsToSave: connectionsToSave,
            GroupsToSave: groupsToSave,
            Failures: failures,
            ImportedCount: imported,
            SkippedCount: skipped,
            RenamedCount: renamed));
    }

    /// <summary>
    /// Builds a <see cref="ConnectionModel"/> from an <see cref="ImportedNode"/>.
    /// Mirrors VM.CreateConnectionModel (VM:394-411) — passwords NEVER imported
    /// (MIG-03), CredentialMode defaults to <see cref="CredentialMode.Own"/>.
    /// </summary>
    private static ConnectionModel CreateConnectionModel(ImportedNode node, Guid? groupId)
    {
        var now = DateTime.UtcNow;
        return new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = node.Name,
            Hostname = node.Hostname ?? string.Empty,
            Port = node.Port,
            Username = node.Username,
            Domain = node.Domain,
            Protocol = node.Protocol,
            GroupId = groupId,
            // MIG-03: passwords never imported. Default to Own — user fills via
            // editor or quick properties.
            CredentialMode = CredentialMode.Own,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Walks the imported tree and yields each Connection-typed leaf paired with
    /// its group id, materializing groups along the way. Mirrors
    /// VM.FlattenCheckedItems (VM:413-440).
    /// </summary>
    private static List<(ImportedNode Node, Guid? GroupId)> FlattenAndEnsureGroups(
        IReadOnlyList<ImportedNode> nodes,
        Guid? parentGroupId,
        Dictionary<string, Guid> groupMap,
        IReadOnlyList<ConnectionGroup> existingGroups,
        List<ConnectionGroup> groupsToSave)
    {
        var result = new List<(ImportedNode, Guid?)>();

        foreach (var node in nodes)
        {
            if (node.Type == ImportNodeType.Container)
            {
                // Create or reuse group, then recurse into children with the new
                // group id.
                var groupId = EnsureGroup(
                    node.Name,
                    parentGroupId,
                    groupMap,
                    existingGroups,
                    groupsToSave);

                result.AddRange(FlattenAndEnsureGroups(
                    node.Children,
                    groupId,
                    groupMap,
                    existingGroups,
                    groupsToSave));
            }
            else
            {
                result.Add((node, parentGroupId));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the existing group's id when one already exists under the same
    /// parent with the same name; otherwise creates a new <see cref="ConnectionGroup"/>,
    /// appends it to <paramref name="groupsToSave"/>, and caches the id.
    /// Mirrors VM.EnsureGroup (VM:442-471).
    /// </summary>
    private static Guid EnsureGroup(
        string name,
        Guid? parentGroupId,
        Dictionary<string, Guid> groupMap,
        IReadOnlyList<ConnectionGroup> existingGroups,
        List<ConnectionGroup> groupsToSave)
    {
        var key = $"{parentGroupId}|{name}";
        if (groupMap.TryGetValue(key, out var existingId))
            return existingId;

        // Check if a group with this name already exists under the same parent.
        var existing = existingGroups.FirstOrDefault(g =>
            g.Name == name && g.ParentGroupId == parentGroupId);
        if (existing is not null)
        {
            groupMap[key] = existing.Id;
            return existing.Id;
        }

        var group = new ConnectionGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentGroupId = parentGroupId,
        };
        groupsToSave.Add(group);
        groupMap[key] = group.Id;
        return group.Id;
    }
}
