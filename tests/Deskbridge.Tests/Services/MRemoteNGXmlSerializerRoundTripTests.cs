using System.IO;
using System.Text;
using System.Xml.Linq;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 Plan 22-03 (D-08, D-10): round-trip integrity + pathological-fixture
/// smoke tests for the new MRemoteNGXmlSerializer reverse-mapper. Validates that
/// the serializer's output is byte-shape-compatible with the existing
/// MRemoteNGImporter parser (no encryption-rejection, attribute fidelity, scale
/// up to 5000 rows) and that the four committed pathological fixtures load via
/// the same parser without failure.
/// </summary>
[Trait("Category", "MRemoteNGXmlSerializerRoundTrip")]
public sealed class MRemoteNGXmlSerializerRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MRemoteNGImporter Importer = new();

    private static string FixturePath(string filename)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "large", filename);

    // -- Encoding / root-attribute fidelity ----------------------------------

    [Fact]
    public void Serialize_Produces_Utf8_NoBom()
    {
        var (conns, groups) = TestDataGenerator.Generate(10, seed: 42);
        using var ms = new MemoryStream();

        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        var bytes = ms.ToArray();
        bytes.Should().NotBeEmpty();

        // UTF-8 BOM is EF BB BF — these three bytes must NOT be present at the start.
        if (bytes.Length >= 3)
        {
            (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .Should().BeFalse("output must be UTF-8 with no BOM (matches sample-confcons.xml)");
        }
    }

    [Fact]
    public void Serialize_Root_FullFileEncryption_IsFalse()
    {
        var (conns, groups) = TestDataGenerator.Generate(10, seed: 42);
        using var ms = new MemoryStream();

        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        ms.Position = 0;
        var doc = XDocument.Load(ms);
        doc.Root.Should().NotBeNull();
        var attr = doc.Root!.Attribute("FullFileEncryption");
        attr.Should().NotBeNull();
        // CRITICAL: must be lowercase "false" — uppercase "True" causes the parser to throw
        // ImportException (MRemoteNGImporter.cs:40).
        attr!.Value.Should().Be("false");
    }

    // -- Round-trip count / hierarchy fidelity --------------------------------

    [Fact]
    public async Task RoundTrip_500_PreservesConnectionCount()
    {
        await AssertRoundTripCountAsync(500);
    }

    [Fact]
    public async Task RoundTrip_1000_PreservesConnectionCount()
    {
        await AssertRoundTripCountAsync(1000);
    }

    [Fact]
    public async Task RoundTrip_5000_PreservesConnectionCount()
    {
        // Stretch case — ~3MB output. Verifies the serializer streams without OOM.
        await AssertRoundTripCountAsync(5000);
    }

    [Fact]
    public async Task RoundTrip_PreservesGroupCount()
    {
        var (conns, groups) = TestDataGenerator.Generate(500, seed: 42);
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        ms.Position = 0;
        var result = await Importer.ParseAsync(ms, Ct);

        result.TotalFolders.Should().Be(groups.Count);
    }

    [Fact]
    public async Task RoundTrip_PreservesGroupHierarchy()
    {
        // TestDataGenerator produces 3-level groups (region -> env -> role). Round-trip
        // must preserve depth: assert at least one root has at least one child group
        // that has at least one child group (3 levels of containers).
        var (conns, groups) = TestDataGenerator.Generate(500, seed: 42);
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        ms.Position = 0;
        var result = await Importer.ParseAsync(ms, Ct);

        int maxDepth = 0;
        foreach (var node in result.RootNodes)
        {
            int d = MeasureContainerDepth(node, 1);
            if (d > maxDepth) maxDepth = d;
        }
        maxDepth.Should().BeGreaterThanOrEqualTo(3, "TestDataGenerator emits 3 levels of groups (region/env/role)");
    }

    private static int MeasureContainerDepth(ImportedNode node, int currentDepth)
    {
        if (node.Type != ImportNodeType.Container)
            return 0;
        int max = currentDepth;
        foreach (var child in node.Children)
        {
            if (child.Type != ImportNodeType.Container)
                continue;
            int d = MeasureContainerDepth(child, currentDepth + 1);
            if (d > max) max = d;
        }
        return max;
    }

    [Fact]
    public async Task RoundTrip_PreservesConnectionAttributes()
    {
        var (conns, groups) = TestDataGenerator.Generate(50, seed: 42);
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        ms.Position = 0;
        var result = await Importer.ParseAsync(ms, Ct);

        // Pick the first connection from the source dataset and find it by name in
        // the parsed tree. Assert Name/Hostname/Port/Username/Domain round-trip exactly.
        var source = conns[0];
        var parsed = FindConnectionByName(result.RootNodes, source.Name);
        parsed.Should().NotBeNull($"connection '{source.Name}' must be preserved through round-trip");
        parsed!.Hostname.Should().Be(source.Hostname);
        parsed.Port.Should().Be(source.Port);
        parsed.Username.Should().Be(source.Username);
        parsed.Domain.Should().Be(source.Domain);
    }

    private static ImportedNode? FindConnectionByName(IReadOnlyList<ImportedNode> nodes, string name)
    {
        foreach (var n in nodes)
        {
            if (n.Type == ImportNodeType.Connection && n.Name == name)
                return n;
            if (n.Children.Count > 0)
            {
                var hit = FindConnectionByName(n.Children, name);
                if (hit is not null)
                    return hit;
            }
        }
        return null;
    }

    [Fact]
    public void Serialize_OmitsPasswordAttribute_OrEmitsEmpty()
    {
        // MIG-03 boundary: serializer must NEVER emit a non-empty Password value, even
        // though the parser ignores the attribute entirely. The serializer is permitted
        // to emit Password="" (cosmetic for fixture realism) OR to omit the attribute.
        var (conns, groups) = TestDataGenerator.Generate(20, seed: 42);
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);

        ms.Position = 0;
        var doc = XDocument.Load(ms);
        foreach (var node in doc.Descendants("Node"))
        {
            var pw = node.Attribute("Password");
            if (pw is not null)
            {
                pw.Value.Should().BeEmpty(
                    "serializer must never emit non-empty Password values (MIG-03)");
            }
        }
    }

    [Theory]
    [InlineData(Protocol.Rdp, "RDP")]
    [InlineData(Protocol.Ssh, "SSH2")]
    [InlineData(Protocol.Vnc, "VNC")]
    public void Serialize_Protocol_MapsToWireValue(Protocol modelProtocol, string expectedWire)
    {
        // Build a single-connection dataset with the target protocol.
        var conn = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "p-test",
            Hostname = "host.local",
            Port = 1234,
            Protocol = modelProtocol,
        };

        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, new[] { conn }, Array.Empty<ConnectionGroup>());

        ms.Position = 0;
        var doc = XDocument.Load(ms);
        var connNode = doc.Descendants("Node")
            .FirstOrDefault(n => n.Attribute("Type")?.Value == "Connection");
        connNode.Should().NotBeNull();
        connNode!.Attribute("Protocol")?.Value.Should().Be(expectedWire);
    }

    // -- Pathological-fixture smoke tests ------------------------------------

    [Fact]
    public async Task Pathological_DeepNest_LoadsWithoutStackOverflow()
    {
        var path = FixturePath("deep-nest-7-levels.xml");
        File.Exists(path).Should().BeTrue($"committed fixture must be copied to {path}");

        await using var stream = File.OpenRead(path);
        var result = await Importer.ParseAsync(stream, Ct);

        result.Should().NotBeNull();
        result.TotalFolders.Should().Be(7);
        result.TotalConnections.Should().Be(1);
    }

    [Fact]
    public async Task Pathological_UnicodeMixed_PreservesUnicodeNames()
    {
        var path = FixturePath("unicode-mixed.xml");
        File.Exists(path).Should().BeTrue();

        await using var stream = File.OpenRead(path);
        var result = await Importer.ParseAsync(stream, Ct);

        result.Should().NotBeNull();
        result.TotalFolders.Should().BeGreaterThanOrEqualTo(1);
        result.TotalConnections.Should().BeGreaterThanOrEqualTo(10);

        // The Cyrillic test row must round-trip its Unicode name through SanitizeName.
        var cyrillic = FindConnectionByName(result.RootNodes, "Тест-сервер");
        cyrillic.Should().NotBeNull("Cyrillic name must survive parser SanitizeName");
        cyrillic!.Hostname.Should().Be("test.kremlin.local");
    }

    [Fact]
    public void Pathological_MalformedSingleRow_LoadsAsValidXml()
    {
        // PATTERNS.md correction #3: this fixture is STRUCTURALLY VALID XML with one
        // SEMANTICALLY-invalid row (Port="abc"). XDocument.Load must not throw.
        var path = FixturePath("malformed-single-row.xml");
        File.Exists(path).Should().BeTrue();

        var act = () => XDocument.Load(path);
        act.Should().NotThrow("the file is structurally valid XML; the bad row is per-row only");
    }

    [Fact]
    public async Task Pathological_MalformedSingleRow_ParserToleratesBadPort()
    {
        // The parser at MRemoteNGImporter.cs:75 uses int.TryParse and defaults Port
        // to 3389 when the attribute can't be parsed. So all 100 rows are present in
        // the parsed tree; the bad-port row simply gets Port=3389. Plan 22-04 will
        // assert the executor's continue-and-collect path on top of this parser tolerance.
        var path = FixturePath("malformed-single-row.xml");
        await using var stream = File.OpenRead(path);
        var result = await Importer.ParseAsync(stream, Ct);

        result.TotalConnections.Should().Be(100);
        result.TotalFolders.Should().Be(1);

        var badRow = FindConnectionByName(result.RootNodes, "BadRow");
        badRow.Should().NotBeNull();
        badRow!.Port.Should().Be(3389, "parser falls back to 3389 on int.TryParse failure");
    }

    [Fact]
    public async Task Pathological_LargeEmptyGroups_ProducesZeroConnections()
    {
        var path = FixturePath("large-empty-groups.xml");
        File.Exists(path).Should().BeTrue();

        await using var stream = File.OpenRead(path);
        var result = await Importer.ParseAsync(stream, Ct);

        result.TotalConnections.Should().Be(0);
        result.TotalFolders.Should().Be(50);
    }

    // -- Helpers --------------------------------------------------------------

    private static async Task AssertRoundTripCountAsync(int n)
    {
        var (conns, groups) = TestDataGenerator.Generate(n, seed: 42);
        conns.Count.Should().Be(n, "TestDataGenerator must produce the requested count");

        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);
        ms.Length.Should().BeGreaterThan(0);

        ms.Position = 0;
        var result = await Importer.ParseAsync(ms, Ct);
        result.TotalConnections.Should().Be(n);
    }
}
