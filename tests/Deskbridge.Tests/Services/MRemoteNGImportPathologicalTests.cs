using System.IO;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 (D-10, IMP-05): pathological-fixture coverage.
/// Each fixture exercises a specific edge case captured by CONTEXT.md D-10.
/// </summary>
[Trait("Category", "Pathological")]
public sealed class MRemoteNGImportPathologicalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Stream OpenFixture(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "large", filename);
        File.Exists(path).Should().BeTrue($"fixture {filename} should be copied to test output");
        return File.OpenRead(path);
    }

    [Fact]
    public async Task DeepNest7Levels_ParsesWithoutStackOverflow()
    {
        using var stream = OpenFixture("deep-nest-7-levels.xml");
        var result = await new MRemoteNGImporter().ParseAsync(stream, Ct);

        result.TotalFolders.Should().Be(7, "fixture has 7 nested Container nodes");
        result.TotalConnections.Should().Be(1, "exactly one Connection node at the bottom");

        // Walk to depth 7 to confirm no recursion was elided
        var current = result.RootNodes[0];
        for (int depth = 1; depth < 7; depth++)
        {
            current.Type.Should().Be(ImportNodeType.Container);
            current.Children.Should().HaveCount(1);
            current = current.Children[0];
        }
        current.Type.Should().Be(ImportNodeType.Container);  // L7
        current.Children.Should().HaveCount(1);
        var leaf = current.Children[0];
        leaf.Type.Should().Be(ImportNodeType.Connection);
        leaf.Hostname.Should().Be("deepleaf.local");
    }

    [Fact]
    public async Task UnicodeMixed_PreservesUnicodeNames()
    {
        using var stream = OpenFixture("unicode-mixed.xml");
        var result = await new MRemoteNGImporter().ParseAsync(stream, Ct);

        // The fixture contains Cyrillic / CJK / RTL / emoji / combining marks.
        // SanitizeName (Phase 7 T-07-10) should NOT strip these — only path
        // separators and control chars.
        var allNodes = FlattenAll(result.RootNodes).ToList();

        allNodes.Any(n => n.Name.Any(c => c is >= 'А' and <= 'я')).Should().BeTrue("Cyrillic preserved");
        // CJK range: U+4E00-U+9FFF (Han) or U+3040-U+309F (Hiragana) or U+30A0-U+30FF (Katakana)
        allNodes.Any(n => n.Name.Any(c => c is >= '぀' and <= '鿿')).Should().BeTrue("CJK preserved");
        // Emoji (surrogate pair check): String.Length > grapheme-count means surrogate pair
        allNodes.Any(n => n.Name.Length > new System.Globalization.StringInfo(n.Name).LengthInTextElements).Should().BeTrue("emoji preserved");
    }

    [Fact]
    public async Task MalformedSingleRow_LoadsAsValidXml_TolerantParser()
    {
        // PATTERNS.md correction #3: this fixture is STRUCTURALLY valid XML.
        // The "malformed" row has Port="abc" — the parser tolerates this and
        // defaults Port to 3389. This test documents the PARSER's tolerance.
        using var stream = OpenFixture("malformed-single-row.xml");
        var result = await new MRemoteNGImporter().ParseAsync(stream, Ct);

        result.TotalConnections.Should().Be(100, "99 valid + 1 with Port=\"abc\" = 100 total");

        // Find the BadRow (only one with Hostname="badrow.local")
        var badRow = FlattenAll(result.RootNodes)
            .First(n => n.Hostname == "badrow.local");
        badRow.Port.Should().Be(3389, "parser falls back to 3389 when Port attribute is not parseable");

        // Drive the executor with this fixture as input — confirm continue-and-collect
        // semantics produce 100 ConnectionsToSave and 0 Failures (because the parser
        // already swallowed the issue at line MRemoteNGImporter.cs:75).
        var executor = new MRemoteNGImportExecutor();
        var request = new ImportRequest(
            result.RootNodes,
            Array.Empty<ConnectionModel>(),
            Array.Empty<ConnectionGroup>(),
            Array.Empty<DuplicateResolution>());
        var executorResult = await executor.PrepareAsync(request, progress: null, ct: Ct);

        executorResult.ConnectionsToSave.Should().HaveCount(100);
        executorResult.Failures.Should().BeEmpty(
            "Phase 22 executor inherits parser tolerances; no stricter post-parse validation in this phase");
    }

    [Fact]
    public async Task LargeEmptyGroups_ProducesZeroConnections()
    {
        using var stream = OpenFixture("large-empty-groups.xml");
        var result = await new MRemoteNGImporter().ParseAsync(stream, Ct);

        result.TotalFolders.Should().Be(50, "fixture has 50 empty Container nodes");
        result.TotalConnections.Should().Be(0);

        // Drive the executor; expect zero ConnectionsToSave + 50 GroupsToSave.
        var executor = new MRemoteNGImportExecutor();
        var request = new ImportRequest(
            result.RootNodes,
            Array.Empty<ConnectionModel>(),
            Array.Empty<ConnectionGroup>(),
            Array.Empty<DuplicateResolution>());
        var executorResult = await executor.PrepareAsync(request, progress: null, ct: Ct);

        executorResult.ConnectionsToSave.Should().BeEmpty();
        executorResult.GroupsToSave.Should().HaveCount(50);
        executorResult.Failures.Should().BeEmpty();
        executorResult.ImportedCount.Should().Be(0);
    }

    private static IEnumerable<ImportedNode> FlattenAll(IEnumerable<ImportedNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenAll(node.Children))
                yield return child;
        }
    }
}
