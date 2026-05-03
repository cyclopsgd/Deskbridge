namespace Deskbridge.Core.Models;

public enum ImportNodeType { Connection, Container }

public record ImportedNode(
    string Name,
    ImportNodeType Type,
    string? Hostname,
    int Port,
    string? Username,
    string? Domain,
    Protocol Protocol,
    string? Description,
    IReadOnlyList<ImportedNode> Children);

public record ImportResult(
    IReadOnlyList<ImportedNode> RootNodes,
    int TotalConnections,
    int TotalFolders);

public class ImportException : Exception
{
    public ImportException(string message) : base(message) { }
    public ImportException(string message, Exception inner) : base(message, inner) { }
}

// ---------------------------------------------------------------------------
// Phase 22 Plan 22-01 (D-01..D-04, D-13..D-15): import-execution surface.
//
// Per PATTERNS.md correction #1, these records co-locate inside ImportModels.cs
// alongside the existing ImportNodeType/ImportedNode/ImportResult/ImportException —
// the project convention is one file per domain area, not one file per type.
// ---------------------------------------------------------------------------

/// <summary>
/// Per-row import failure category. UI displays only <see cref="FailedImport.Detail"/>;
/// <see cref="FailedImport.Type"/> is for Serilog structuring and future filtering (D-14).
/// Open enum extension expected — append new members; do not reorder existing ones.
/// </summary>
public enum ImportFailureType { Duplicate, MalformedXml, ValidationError, Unknown }

/// <summary>
/// One row that did not import cleanly. The executor collects failures rather than
/// aborting the whole import (D-07 continue-and-collect). Surfaced in Step 4 of the
/// import wizard (Phase 22 Plan 22-02).
/// </summary>
public record FailedImport(
    string ConnectionName,
    ImportFailureType Type,
    string Detail);

/// <summary>
/// User decision for a duplicate hostname encountered during import.
/// Lifted into Deskbridge.Core.Models from the obsolete copy in ImportWizardViewModel.cs
/// (the duplicate at the view-tier will be removed by Plan 22-02).
/// </summary>
public enum DuplicateAction { Skip, Overwrite, Rename }

/// <summary>
/// Per-hostname duplicate resolution supplied by the user before the executor runs.
/// </summary>
public record DuplicateResolution(
    string Hostname,
    DuplicateAction Action);

/// <summary>
/// Inputs for <c>IImportExecutor.PrepareAsync</c>. Carries the already-parsed
/// node tree, the existing store snapshot, and the per-hostname duplicate resolutions.
/// </summary>
public record ImportRequest(
    IReadOnlyList<ImportedNode> CheckedNodes,
    IReadOnlyList<ConnectionModel> ExistingConnections,
    IReadOnlyList<ConnectionGroup> ExistingGroups,
    IReadOnlyList<DuplicateResolution> Resolutions);

/// <summary>
/// Output of the prepare loop — ready for the VM to hand to
/// <c>IConnectionStore.SaveBatch</c> (D-02). The executor never persists.
/// </summary>
public record ImportPrepareResult(
    IReadOnlyList<ConnectionModel> ConnectionsToSave,
    IReadOnlyList<ConnectionGroup> GroupsToSave,
    IReadOnlyList<FailedImport> Failures,
    int ImportedCount,
    int SkippedCount,
    int RenamedCount);
