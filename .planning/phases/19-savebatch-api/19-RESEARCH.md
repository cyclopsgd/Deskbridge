# Phase 19: SaveBatch API - Research

**Researched:** 2026-04-26
**Domain:** Data layer batch persistence (System.Text.Json, atomic file I/O)
**Confidence:** HIGH

## Summary

Phase 19 adds a `SaveBatch` method to `IConnectionStore` that accepts multiple connections (and optionally groups) and persists them in a single atomic file write. The current import flow calls `_store.Save()` per connection, each triggering a full JSON serialize + tmp-file-write + File.Move cycle. For 500+ connections this means 500+ full-file rewrites of increasingly large JSON.

The implementation is straightforward: follow the `DeleteBatch` pattern already established in the codebase. Mutate the in-memory `_data.Connections` list via upsert-by-Id, then call `PersistAtomically()` once at the end. A new `ConnectionDataChangedEvent` must be introduced and published after the batch completes so the tree UI refreshes. The search index (`ConnectionQueryService`) reads live from the store and requires no refresh.

**Primary recommendation:** Add `SaveBatch` to `IConnectionStore` and `JsonConnectionStore` mirroring the `DeleteBatch` pattern, introduce `ConnectionDataChangedEvent`, wire the tree ViewModel to subscribe to it, and migrate the import wizard to call `SaveBatch` instead of per-item `Save`.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| IMP-04 | User's 500+ connection import completes via a single batch file write instead of one write per connection (SaveBatch API) | SaveBatch method on IConnectionStore + JsonConnectionStore implementation using existing PersistAtomically pattern; import wizard call site migrated from per-item Save loop to single SaveBatch call |
</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Batch persistence API | Data Layer (Deskbridge.Core) | -- | IConnectionStore interface + JsonConnectionStore are both in Core; pure data concern |
| ConnectionDataChangedEvent | Data Layer (Deskbridge.Core) | -- | Event record lives in Core/Events alongside existing events |
| Tree refresh on data change | Frontend ViewModel | -- | ConnectionTreeViewModel subscribes to event, calls RefreshTree on Dispatcher |
| Import wizard migration | Frontend ViewModel | -- | ImportWizardViewModel changes Save loop to SaveBatch call |

## Standard Stack

No new packages required. This phase operates entirely within the existing stack:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Text.Json | in-box (.NET 10) | JSON serialization for connections.json | Already used by JsonConnectionStore; no changes needed |
| CommunityToolkit.Mvvm | 8.4.2 | WeakReferenceMessenger for event bus | Already registered; ConnectionDataChangedEvent uses existing IEventBus |

**Installation:** None -- all dependencies already present.

## Architecture Patterns

### System Architecture Diagram

```
Import Wizard / Bulk Edit
        |
        | SaveBatch(connections, groups)
        v
  IConnectionStore (interface)
        |
        v
  JsonConnectionStore
        |
        |  1. Upsert connections in-memory (loop, no I/O)
        |  2. Upsert groups in-memory (loop, no I/O)
        |  3. PersistAtomically() -- single JSON write
        |
        v
  connections.json (atomic tmp+rename)
        |
        | (caller publishes ConnectionDataChangedEvent)
        v
  IEventBus.Publish<ConnectionDataChangedEvent>()
        |
        +---> ConnectionTreeViewModel.OnDataChanged()
        |         -> Dispatcher.InvokeAsync(RefreshTree)
        |
        +---> ToastSubscriptionService (existing ConnectionImportedEvent still fires separately)
        |
        +---> ConnectionQueryService: NO subscription needed
                  (reads live from store on every query)
```

### Pattern 1: Batch Upsert (Mirror DeleteBatch)

**What:** Modify the in-memory collection in a loop, persist once at the end.
**When to use:** Any operation that touches multiple connections/groups in one logical action.
**Example:**
```csharp
// Source: existing DeleteBatch pattern in JsonConnectionStore.cs
public void SaveBatch(IEnumerable<ConnectionModel> connections, IEnumerable<ConnectionGroup> groups)
{
    // Phase 1: Upsert groups in-memory (no I/O)
    foreach (var group in groups)
    {
        var existing = _data.Groups.FindIndex(g => g.Id == group.Id);
        if (existing >= 0)
            _data.Groups[existing] = group;
        else
            _data.Groups.Add(group);
    }

    // Phase 2: Upsert connections in-memory (no I/O)
    foreach (var connection in connections)
    {
        var existing = _data.Connections.FindIndex(c => c.Id == connection.Id);
        if (existing >= 0)
        {
            connection.UpdatedAt = DateTime.UtcNow;
            _data.Connections[existing] = connection;
        }
        else
        {
            _data.Connections.Add(connection);
        }
    }

    // Phase 3: Single atomic file write
    PersistAtomically();
}
```

### Pattern 2: Event-Driven Tree Refresh

**What:** Publish a data-changed event after batch write; tree ViewModel subscribes and refreshes.
**When to use:** After any bulk data mutation that the tree must reflect.
**Example:**
```csharp
// In ConnectionTreeViewModel constructor (Deskbridge project)
_eventBus.Subscribe<ConnectionDataChangedEvent>(this, OnDataChanged);

// Handler
private void OnDataChanged(ConnectionDataChangedEvent e)
{
    // RefreshTree must run on the UI thread
    DispatchToUi(() => RefreshTree());
}
```

### Anti-Patterns to Avoid
- **Publishing from inside the store:** `JsonConnectionStore` currently has no `IEventBus` dependency. Adding it creates a coupling between data layer and eventing. Keep event publishing in the caller (ViewModel layer), matching the existing pattern where `ImportWizardViewModel` publishes `ConnectionImportedEvent`. [ASSUMED]
- **Calling Save() in a loop for batch operations:** This is the exact problem being fixed. Each `Save()` serializes the entire file. 500 connections = 500 full file writes.
- **Separate "batch mode" flag:** Some patterns use a begin/end batch mode. Unnecessary here -- a dedicated `SaveBatch` method is simpler and matches the `DeleteBatch` precedent.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Atomic file write | Custom file locking or journaling | Existing `PersistAtomically()` (tmp + File.Move) | Already implemented and tested; File.Move with overwrite is atomic on NTFS |
| Event bus | Custom event aggregator | Existing `IEventBus` (WeakReferenceMessenger) | Already wired through DI, handles weak references |
| JSON serialization | Custom serializer | Existing `_jsonOptions` in JsonConnectionStore | Consistent formatting, camelCase policy, relaxed encoding already configured |

**Key insight:** This phase has zero new infrastructure. Every building block (atomic write, event bus, upsert-by-Id, Dispatcher marshaling) is already implemented and tested.

## Common Pitfalls

### Pitfall 1: UpdatedAt Semantics in Batch Upsert
**What goes wrong:** Setting `UpdatedAt = DateTime.UtcNow` on all items in the batch makes `GetRecent()` useless (every connection gets the same timestamp).
**Why it happens:** Copy-pasting the single-`Save` pattern without thinking about batch semantics.
**How to avoid:** Only set `UpdatedAt` on the upsert (update) path, not on new connections being added. Match the existing `Save()` behavior exactly. New connections already have `UpdatedAt` set at creation time by the caller.
**Warning signs:** After import, `GetRecent()` returns connections in arbitrary order instead of most-recently-touched first.

### Pitfall 2: Thread Marshaling on Tree Refresh
**What goes wrong:** `RefreshTree()` touches `ObservableCollection<TreeItemViewModel>` which is UI-bound. Calling it from a non-UI thread throws `InvalidOperationException`.
**Why it happens:** The `ConnectionDataChangedEvent` handler may fire on whatever thread published it. If `SaveBatch` is called from a background thread (future async import), the handler runs there too.
**How to avoid:** Use the existing `DispatchToUi()` helper in `ConnectionTreeViewModel` (line ~387) to marshal `RefreshTree()` to the Dispatcher.
**Warning signs:** Intermittent crashes after import completes.

### Pitfall 3: Groups Must Be Saved Before Connections
**What goes wrong:** If connections reference a `GroupId` for a group being saved in the same batch, and groups are persisted after connections, the in-memory state is transiently inconsistent.
**Why it happens:** `SaveBatch` processes connections before groups.
**How to avoid:** Process groups first, then connections, mirroring the `DeleteBatch` approach (groups first, connections second).
**Warning signs:** Import of nested hierarchies produces orphaned connections.

### Pitfall 4: Forgetting to Publish the Event
**What goes wrong:** `SaveBatch` completes successfully but the tree doesn't update.
**Why it happens:** The event publishing responsibility is on the caller. Easy to forget when adding a new call site.
**How to avoid:** Document clearly in the method's XML doc comment that callers must publish `ConnectionDataChangedEvent` after calling `SaveBatch`. Consider a helper method or extension that combines both.
**Warning signs:** Tree shows stale data after bulk operations; user has to click away and back.

## Code Examples

### Current Write Amplification (the problem)
```csharp
// Source: ImportWizardViewModel.cs lines 308-356
// Each _store.Save(conn) triggers PersistAtomically() -> full JSON serialize + write
foreach (var (item, groupId) in flatItems)
{
    var newConn = CreateConnectionModel(item, groupId);
    _store.Save(newConn);  // <-- Full file write per connection!
}
```

### Fixed Batch Import (the solution)
```csharp
// Collect all connections and groups first, then persist once
var connectionsToSave = new List<ConnectionModel>();
var groupsToSave = new List<ConnectionGroup>();

foreach (var (item, groupId) in flatItems)
{
    if (item.Type == ImportNodeType.Container)
    {
        groupsToSave.Add(/* ... */);
    }
    else
    {
        connectionsToSave.Add(CreateConnectionModel(item, groupId));
    }
}

_store.SaveBatch(connectionsToSave, groupsToSave);
_bus.Publish(new ConnectionDataChangedEvent());
```

### ConnectionDataChangedEvent Definition
```csharp
// In Deskbridge.Core/Events/ConnectionEvents.cs
/// <summary>
/// Published after a bulk data mutation (SaveBatch, DeleteBatch) completes.
/// Subscribers should refresh their view of the connection store.
/// </summary>
public record ConnectionDataChangedEvent();
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-item Save in import loop | (this phase) SaveBatch for single write | Phase 19 | 500x fewer file writes during import |
| No data-changed event | ConnectionDataChangedEvent | Phase 19 | Tree refreshes after any bulk mutation |

**Deprecated/outdated:** None -- this is additive.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Event publishing should remain in the caller (ViewModel), not in JsonConnectionStore | Architecture Patterns / Anti-Patterns | If store should publish, need to inject IEventBus into JsonConnectionStore (adds coupling to data layer) |
| A2 | ConnectionDataChangedEvent should be a marker event with no payload | Code Examples | If callers need changed IDs list, event shape needs redesign |
| A3 | SaveBatch should accept both connections AND groups (not connections-only) | Architecture Patterns | If connections-only, import wizard still has per-group write amplification via SaveGroup calls |

## Open Questions

1. **Should SaveBatch accept connections + groups, or connections only?**
   - What we know: The requirement text (IMP-04) says "single batch file write instead of one write per connection." The existing `DeleteBatch` accepts both connections and groups. The import wizard creates groups via `EnsureGroup` -> `_store.SaveGroup()` which also triggers per-group file writes.
   - What's unclear: Whether the phase scope extends to group batching.
   - Recommendation: Accept both (connections + groups) for symmetry with `DeleteBatch` and to fully eliminate write amplification during import. The implementation cost is minimal (upsert loop for groups is identical pattern).

2. **Where should ConnectionDataChangedEvent be published -- store or caller?**
   - What we know: The existing pattern is caller-publishes (`ImportWizardViewModel` publishes `ConnectionImportedEvent`; `JsonConnectionStore` has no IEventBus dependency). The success criteria says "a ConnectionDataChangedEvent is published after SaveBatch completes."
   - What's unclear: Whether "after SaveBatch completes" means inside SaveBatch or by the caller after calling SaveBatch.
   - Recommendation: Caller-publishes to match existing patterns and avoid adding IEventBus dependency to the data layer. Document responsibility in SaveBatch's XML doc comment.

3. **Should the import wizard be migrated in this phase?**
   - What we know: IMP-04's user-visible text says "User's 500+ connection import completes via a single batch file write." Without migrating the import wizard call site, the API exists but IMP-04 is not user-visible yet.
   - What's unclear: Whether this phase is API-only (plumbing for Phase 22) or should include the consumer migration.
   - Recommendation: Migrate the import wizard in this phase. The SC says "Developer can call SaveBatch" (API exists) but IMP-04 requires the actual import to use it. Phase 22 adds progress bars and stress tests, not the SaveBatch call itself.

4. **Should existing single-item Save/SaveGroup/Delete/DeleteGroup also publish ConnectionDataChangedEvent?**
   - What we know: Currently, after calling `_store.Save()` from the tree ViewModel, RefreshTree is called explicitly. The new event would be redundant for single-item operations where the caller already refreshes.
   - What's unclear: Whether a future refactor should standardize all data mutations through the event.
   - Recommendation: Out of scope for this phase. Only SaveBatch callers publish the event. Existing single-item paths continue calling RefreshTree directly.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit.v3 + FluentAssertions + NSubstitute |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "Category=SaveBatch" -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| IMP-04-a | SaveBatch upserts N connections in single file write | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |
| IMP-04-b | SaveBatch round-trips through load (persistence verified) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |
| IMP-04-c | SaveBatch handles mixed insert/update (upsert-by-Id) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |
| IMP-04-d | SaveBatch groups before connections ordering | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |
| IMP-04-e | ConnectionDataChangedEvent published after batch | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |
| IMP-04-f | Tree refreshes on ConnectionDataChangedEvent | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~SaveBatchTests" -x` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "Category=SaveBatch" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Services/SaveBatchTests.cs` -- covers IMP-04-a through IMP-04-d
- [ ] Event publish + tree refresh tests in same or separate file -- covers IMP-04-e, IMP-04-f

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | -- |
| V3 Session Management | no | -- |
| V4 Access Control | no | -- |
| V5 Input Validation | no | Input already validated by import parser (MRemoteNGImporter sanitizes names); SaveBatch operates on already-constructed ConnectionModel objects |
| V6 Cryptography | no | -- |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Credential leak in batch write | Information Disclosure | ConnectionModel never contains passwords (CredentialMode only); JSON serialization verified credential-free in LOG-05 |
| TOCTOU on atomic write | Tampering | Existing tmp+File.Move pattern; no change needed |

No new security surface introduced by this phase.

## Sources

### Primary (HIGH confidence)
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` -- current interface (6 methods, no SaveBatch)
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs` -- current implementation with PersistAtomically, Save, DeleteBatch
- `src/Deskbridge/ViewModels/ImportWizardViewModel.cs` -- current import loop (per-item Save at line 315-356)
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` -- RefreshTree pattern, event subscriptions, DispatchToUi helper
- `src/Deskbridge.Core/Services/ConnectionQueryService.cs` -- reads live from store (no refresh needed)
- `src/Deskbridge.Core/Events/ConnectionEvents.cs` -- existing event records (ConnectionDataChangedEvent does not exist yet)
- `tests/Deskbridge.Tests/Services/BulkDeleteTests.cs` -- test patterns for batch operations
- `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs` -- test patterns for store operations

### Secondary (MEDIUM confidence)
- None needed -- all findings are from codebase inspection

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages; mirrors existing patterns exactly
- Architecture: HIGH -- follows established DeleteBatch pattern; all building blocks exist
- Pitfalls: HIGH -- identified from direct codebase inspection of existing code paths

**Research date:** 2026-04-26
**Valid until:** 2026-05-26 (stable -- data layer patterns unlikely to change)
