using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 Plan 22-01 (D-01..D-04, D-06, D-07, D-13..D-15): RED-phase tests
/// for the new <see cref="MRemoteNGImportExecutor"/>. Tests assert:
///
/// (a) Happy-path 500-row produces all connections (D-01 prepare loop).
/// (b) Per-row IProgress&lt;int&gt; reporting (D-03).
/// (c) Executor takes NO IConnectionStore — VM owns SaveBatch (D-02 compile-time guarantee).
/// (d) Skip / Rename / Overwrite / auto-rename branches preserve VM:300-361 semantics.
/// (e) Per-row exceptions go to the Failures list — loop continues (D-07 continue-and-collect).
/// (f) Pre-cancelled CT throws OperationCanceledException at loop entry (D-06).
/// (g) Non-RDP nodes are silently filtered (existing IsSupported gate).
/// (h) Container nodes produce GroupsToSave entries with correct ParentGroupId chains.
/// </summary>
[Trait("Category", "MRemoteNGImportExecutor")]
public sealed class MRemoteNGImportExecutorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- helpers ---------------------------------------------------------

    private static ImportedNode MakeConn(
        string name,
        string hostname,
        Protocol protocol = Protocol.Rdp,
        int port = 3389)
        => new(
            Name: name,
            Type: ImportNodeType.Connection,
            Hostname: hostname,
            Port: port,
            Username: null,
            Domain: null,
            Protocol: protocol,
            Description: null,
            Children: Array.Empty<ImportedNode>());

    private static ImportedNode MakeContainer(string name, params ImportedNode[] children)
        => new(
            Name: name,
            Type: ImportNodeType.Container,
            Hostname: null,
            Port: 3389,
            Username: null,
            Domain: null,
            Protocol: Protocol.Rdp,
            Description: null,
            Children: children);

    private static ImportRequest Request(
        IReadOnlyList<ImportedNode> roots,
        IReadOnlyList<ConnectionModel>? existing = null,
        IReadOnlyList<ConnectionGroup>? existingGroups = null,
        params (string Hostname, DuplicateAction Action)[] resolutions)
        => new(
            CheckedNodes: roots,
            ExistingConnections: existing ?? Array.Empty<ConnectionModel>(),
            ExistingGroups: existingGroups ?? Array.Empty<ConnectionGroup>(),
            Resolutions: resolutions
                .Select(r => new DuplicateResolution(r.Hostname, r.Action))
                .ToList());

    private sealed class CapturingProgress : IProgress<int>
    {
        public List<int> Reports { get; } = new();
        public void Report(int v) => Reports.Add(v);
    }

    // --- tests -----------------------------------------------------------

    [Fact]
    public async Task PrepareAsync_HappyPath_500_ProducesAllConnections()
    {
        var nodes = Enumerable.Range(0, 500)
            .Select(i => MakeConn($"Server{i:D4}", $"host{i:D4}.lab.local"))
            .ToList<ImportedNode>();

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(Request(nodes), progress: null, Ct);

        result.ConnectionsToSave.Should().HaveCount(500);
        result.Failures.Should().BeEmpty();
        result.ImportedCount.Should().Be(500);
        result.SkippedCount.Should().Be(0);
        result.RenamedCount.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_ReportsProgressPerRow()
    {
        var nodes = Enumerable.Range(0, 25)
            .Select(i => MakeConn($"S{i}", $"h{i}"))
            .ToList<ImportedNode>();

        var progress = new CapturingProgress();
        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(Request(nodes), progress, Ct);

        progress.Reports.Should().NotBeEmpty();
        progress.Reports[^1].Should().Be(25);
        result.ConnectionsToSave.Should().HaveCount(25);
    }

    [Fact]
    public void PrepareAsync_DoesNotCallSaveBatch()
    {
        // Compile-time guarantee + reflection check: the executor's only public
        // constructor takes ZERO parameters — no IConnectionStore can be injected.
        // VM owns the SaveBatch call at the end of ImportSelectedAsync (D-02).
        var ctors = typeof(MRemoteNGImportExecutor).GetConstructors();
        ctors.Should().HaveCount(1);
        ctors[0].GetParameters().Should().BeEmpty(
            "executor must not depend on IConnectionStore — VM owns SaveBatch (D-02)");
    }

    [Fact]
    public async Task PrepareAsync_DuplicateWithSkipResolution_IncrementsSkipped()
    {
        var existing = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Existing", Hostname = "dup.lab.local" },
        };
        var nodes = new List<ImportedNode> { MakeConn("New", "dup.lab.local") };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(
            Request(nodes, existing, resolutions: ("dup.lab.local", DuplicateAction.Skip)),
            null, Ct);

        result.SkippedCount.Should().Be(1);
        result.ImportedCount.Should().Be(0);
        result.RenamedCount.Should().Be(0);
        result.ConnectionsToSave.Should().BeEmpty();
        result.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_DuplicateWithRenameResolution_IncrementsRenamed()
    {
        var existing = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Existing", Hostname = "dup.lab.local" },
        };
        var nodes = new List<ImportedNode> { MakeConn("New", "dup.lab.local") };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(
            Request(nodes, existing, resolutions: ("dup.lab.local", DuplicateAction.Rename)),
            null, Ct);

        result.ImportedCount.Should().Be(1);
        result.RenamedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        result.ConnectionsToSave.Should().HaveCount(1);
        result.ConnectionsToSave[0].Name.Should().Be("New (imported)");
    }

    [Fact]
    public async Task PrepareAsync_DuplicateWithOverwriteResolution_UpdatesExisting()
    {
        var existingId = Guid.NewGuid();
        var beforeUpdated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new List<ConnectionModel>
        {
            new()
            {
                Id = existingId,
                Name = "Old name",
                Hostname = "dup.lab.local",
                Port = 3389,
                UpdatedAt = beforeUpdated,
            },
        };
        var nodes = new List<ImportedNode>
        {
            new(
                Name: "Updated",
                Type: ImportNodeType.Connection,
                Hostname: "dup.lab.local",
                Port: 3390,
                Username: "alice",
                Domain: "CORP",
                Protocol: Protocol.Rdp,
                Description: null,
                Children: Array.Empty<ImportedNode>()),
        };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(
            Request(nodes, existing, resolutions: ("dup.lab.local", DuplicateAction.Overwrite)),
            null, Ct);

        result.ImportedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        result.RenamedCount.Should().Be(0);
        result.ConnectionsToSave.Should().HaveCount(1);

        var saved = result.ConnectionsToSave[0];
        saved.Id.Should().Be(existingId, "Overwrite mutates the existing instance in place");
        saved.Name.Should().Be("Updated");
        saved.Port.Should().Be(3390);
        saved.Username.Should().Be("alice");
        saved.Domain.Should().Be("CORP");
        saved.UpdatedAt.Should().BeAfter(beforeUpdated);
    }

    [Fact]
    public async Task PrepareAsync_DuplicateWithNoResolution_AutoRenames()
    {
        var existing = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Existing", Hostname = "dup.lab.local" },
        };
        var nodes = new List<ImportedNode> { MakeConn("New", "dup.lab.local") };

        var executor = new MRemoteNGImportExecutor();
        // No resolutions provided — executor falls through the auto-rename branch
        // (mirrors ImportWizardViewModel.cs:346-352).
        var result = await executor.PrepareAsync(Request(nodes, existing), null, Ct);

        result.ImportedCount.Should().Be(1);
        result.RenamedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        result.ConnectionsToSave.Should().HaveCount(1);
        result.ConnectionsToSave[0].Name.Should().Be("New (imported)");
    }

    [Fact]
    public async Task PrepareAsync_RowTransformThrows_CollectsFailureContinuesLoop()
    {
        // Synthesize 9 good rows + 1 row whose transform will throw.
        // The deterministic way to force a transform failure is to feed a node
        // whose Hostname triggers a guarded synthetic exception. We use a
        // sentinel Hostname value "@@throw@@" the executor's guard can detect,
        // OR — simpler — a node with an oversize port that overflows int parsing
        // is not feasible because Port is already int. The cleanest mechanism:
        // pass a node whose Name is null. C# records permit null because
        // `string Name` is a non-nullable reference but null can still leak through
        // a null-forgiving cast. We construct via the positional ctor with `null!`
        // to force the per-row try/catch.
        var nodes = new List<ImportedNode>
        {
            MakeConn("Good1", "h1"),
            MakeConn("Good2", "h2"),
            MakeConn("Good3", "h3"),
            MakeConn("Good4", "h4"),
            MakeConn("Good5", "h5"),
            // Bad row: null Name will cause CreateConnectionModel.Name = null,
            // which the prepare loop's per-row try/catch is meant to swallow.
            new(
                Name: null!,
                Type: ImportNodeType.Connection,
                Hostname: "bad.host",
                Port: 3389,
                Username: null,
                Domain: null,
                Protocol: Protocol.Rdp,
                Description: null,
                Children: Array.Empty<ImportedNode>()),
            MakeConn("Good6", "h6"),
            MakeConn("Good7", "h7"),
            MakeConn("Good8", "h8"),
            MakeConn("Good9", "h9"),
        };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(Request(nodes), null, Ct);

        // Loop continued past the bad row.
        result.ConnectionsToSave.Should().HaveCount(9);
        result.ImportedCount.Should().Be(9);
        result.Failures.Should().HaveCount(1);
        result.Failures[0].Type.Should().Be(ImportFailureType.Unknown);
    }

    [Fact]
    public async Task PrepareAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var nodes = new List<ImportedNode> { MakeConn("X", "h") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var executor = new MRemoteNGImportExecutor();
        var act = async () => await executor.PrepareAsync(Request(nodes), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PrepareAsync_NonRdpProtocol_SkipsSilently()
    {
        var nodes = new List<ImportedNode>
        {
            MakeConn("RdpOk", "rdp.host"),
            MakeConn("SshSkip", "ssh.host", Protocol.Ssh),
            MakeConn("VncSkip", "vnc.host", Protocol.Vnc),
        };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(Request(nodes), null, Ct);

        result.ConnectionsToSave.Should().HaveCount(1);
        result.ConnectionsToSave[0].Hostname.Should().Be("rdp.host");
        // Non-RDP nodes filtered before the row is "processed" — NOT failures.
        result.Failures.Should().BeEmpty();
        result.ImportedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_GroupsTopologyPreserved()
    {
        // Tree:
        //   "Production"  (container)
        //     "Web Tier"  (container)
        //       conn1
        //       conn2
        //     conn3   (directly in Production)
        var nodes = new List<ImportedNode>
        {
            MakeContainer("Production",
                MakeContainer("Web Tier",
                    MakeConn("conn1", "h1"),
                    MakeConn("conn2", "h2")),
                MakeConn("conn3", "h3")),
        };

        var executor = new MRemoteNGImportExecutor();
        var result = await executor.PrepareAsync(Request(nodes), null, Ct);

        result.ConnectionsToSave.Should().HaveCount(3);
        result.GroupsToSave.Should().HaveCount(2);

        var production = result.GroupsToSave.Single(g => g.Name == "Production");
        var webTier = result.GroupsToSave.Single(g => g.Name == "Web Tier");

        production.ParentGroupId.Should().BeNull("Production is a top-level group");
        webTier.ParentGroupId.Should().Be(production.Id, "Web Tier is nested under Production");

        var conn1 = result.ConnectionsToSave.Single(c => c.Hostname == "h1");
        var conn2 = result.ConnectionsToSave.Single(c => c.Hostname == "h2");
        var conn3 = result.ConnectionsToSave.Single(c => c.Hostname == "h3");

        conn1.GroupId.Should().Be(webTier.Id);
        conn2.GroupId.Should().Be(webTier.Id);
        conn3.GroupId.Should().Be(production.Id);
    }
}
