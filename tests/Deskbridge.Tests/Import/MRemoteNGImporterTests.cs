using System.IO;
using System.Text;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Import;

public class MRemoteNGImporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MRemoteNGImporter _importer = new();

    private static Stream GetFixtureStream()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-confcons.xml");
        return File.OpenRead(path);
    }

    private static MemoryStream ToStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    // Test 1: ParseAsync with valid confCons.xml returns ImportResult with correct total connections count
    [Fact]
    public async Task ParseAsync_ValidFile_ReturnsTotalConnectionsCount()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);
        result.TotalConnections.Should().Be(5);
    }

    // Test 2: ParseAsync with valid confCons.xml returns ImportResult with correct total folders count
    [Fact]
    public async Task ParseAsync_ValidFile_ReturnsTotalFoldersCount()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);
        result.TotalFolders.Should().Be(2);
    }

    // Test 3: ParseAsync preserves folder/connection hierarchy (children nested under containers)
    [Fact]
    public async Task ParseAsync_ValidFile_PreservesHierarchy()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        // Root should have 3 items: 2 containers + 1 connection
        result.RootNodes.Should().HaveCount(3);

        // First container "Production Servers" should have 2 children
        var prod = result.RootNodes[0];
        prod.Name.Should().Be("Production Servers");
        prod.Type.Should().Be(ImportNodeType.Container);
        prod.Children.Should().HaveCount(2);

        // Second container "Dev Environment" should have 2 children
        var dev = result.RootNodes[1];
        dev.Name.Should().Be("Dev Environment");
        dev.Type.Should().Be(ImportNodeType.Container);
        dev.Children.Should().HaveCount(2);

        // Third item is a standalone connection
        var standalone = result.RootNodes[2];
        standalone.Name.Should().Be("Standalone Server");
        standalone.Type.Should().Be(ImportNodeType.Connection);
        standalone.Children.Should().BeEmpty();
    }

    // Test 4: ParseAsync maps RDP protocol correctly
    [Fact]
    public async Task ParseAsync_RdpProtocol_MappedCorrectly()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var webServer = result.RootNodes[0].Children[0];
        webServer.Protocol.Should().Be(Protocol.Rdp);
    }

    // Test 5: ParseAsync maps SSH2 protocol to Protocol.Ssh
    [Fact]
    public async Task ParseAsync_Ssh2Protocol_MappedToSsh()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var devLinux = result.RootNodes[1].Children[0];
        devLinux.Name.Should().Be("Dev Linux Box");
        devLinux.Protocol.Should().Be(Protocol.Ssh);
    }

    // Test 6: ParseAsync maps unknown protocol to Protocol.Rdp (default)
    [Fact]
    public async Task ParseAsync_UnknownProtocol_DefaultsToRdp()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
                <Node Name="Telnet Box" Type="Connection"
                      Protocol="Telnet" Hostname="telnet.local" Port="23"
                      Username="" Domain="" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);
        var result = await _importer.ParseAsync(stream, Ct);

        result.RootNodes[0].Protocol.Should().Be(Protocol.Rdp);
    }

    // Test 7: ParseAsync ignores Password attribute (imported nodes have no password data)
    [Fact]
    public async Task ParseAsync_PasswordAttribute_IsIgnored()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        // ImportedNode record has no password field -- the fact that it compiles
        // and returns data without password proves the attribute is skipped.
        // We verify the node otherwise parses correctly.
        var webServer = result.RootNodes[0].Children[0];
        webServer.Name.Should().Be("Web Server 01");
        webServer.Hostname.Should().Be("web01.prod.local");
    }

    // Test 8: ParseAsync with FullFileEncryption="True" throws ImportException with user-friendly message
    [Fact]
    public async Task ParseAsync_EncryptedFile_ThrowsImportException()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="True" ConfVersion="2.6">
                <Node Name="Server" Type="Connection" Protocol="RDP"
                      Hostname="server.local" Port="3389" Username="" Domain="" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);

        var act = () => _importer.ParseAsync(stream, Ct);

        await act.Should().ThrowAsync<ImportException>()
            .WithMessage("*encrypted*export*unencrypted*");
    }

    // Test 9: ParseAsync with invalid XML (non-XML content) throws ImportException
    [Fact]
    public async Task ParseAsync_InvalidXml_ThrowsImportException()
    {
        using var stream = ToStream("this is not valid xml content at all");

        var act = () => _importer.ParseAsync(stream, Ct);

        await act.Should().ThrowAsync<ImportException>()
            .WithMessage("*not a valid mRemoteNG configuration file*");
    }

    // Test 10: ParseAsync maps Name, Hostname, Port, Username, Domain correctly
    [Fact]
    public async Task ParseAsync_ValidFile_MapsAttributesCorrectly()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var webServer = result.RootNodes[0].Children[0];
        webServer.Name.Should().Be("Web Server 01");
        webServer.Hostname.Should().Be("web01.prod.local");
        webServer.Port.Should().Be(3389);
        webServer.Username.Should().Be("admin");
        webServer.Domain.Should().Be("PROD");
    }

    // Test 11: ParseAsync handles missing optional attributes gracefully (null hostname, default port 3389)
    [Fact]
    public async Task ParseAsync_MissingOptionalAttributes_HandledGracefully()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
                <Node Name="Minimal" Type="Connection" Protocol="RDP" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);
        var result = await _importer.ParseAsync(stream, Ct);

        var node = result.RootNodes[0];
        node.Name.Should().Be("Minimal");
        node.Hostname.Should().BeNull();
        node.Port.Should().Be(3389);
        node.Username.Should().BeNull();
        node.Domain.Should().BeNull();
    }

    // Test 12: SourceName returns "mRemoteNG"
    [Fact]
    public void SourceName_ReturnsMRemoteNG()
    {
        _importer.SourceName.Should().Be("mRemoteNG");
    }

    // Test 13: FileFilter returns "mRemoteNG Config (*.xml)|*.xml"
    [Fact]
    public void FileFilter_ReturnsCorrectFilter()
    {
        _importer.FileFilter.Should().Be("mRemoteNG Config (*.xml)|*.xml");
    }

    // ---------------------------------------------------------------- Inheritance parsing (260428-oga)

    // Test A: Inheritance="Username,Password,Domain,Resolution" -> InheritsCredentials = true (fixture: Web Server 01)
    [Fact]
    public async Task ParseAsync_InheritanceWithUsernameAndPasswordTokens_SetsInheritsCredentialsTrue()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var webServer = result.RootNodes[0].Children[0];
        webServer.Name.Should().Be("Web Server 01");
        webServer.InheritsCredentials.Should().BeTrue(
            "Inheritance attribute lists Username and Password tokens");
    }

    // Test B: Inheritance="None" -> InheritsCredentials = false (fixture: DB Server 01)
    [Fact]
    public async Task ParseAsync_InheritanceNone_SetsInheritsCredentialsFalse()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var dbServer = result.RootNodes[0].Children[1];
        dbServer.Name.Should().Be("DB Server 01");
        dbServer.InheritsCredentials.Should().BeFalse(
            "Inheritance=\"None\" means do not inherit");
    }

    // Test C: Missing Inheritance attribute -> InheritsCredentials = false (fixture: Standalone Server)
    [Fact]
    public async Task ParseAsync_MissingInheritanceAttribute_SetsInheritsCredentialsFalse()
    {
        using var stream = GetFixtureStream();
        var result = await _importer.ParseAsync(stream, Ct);

        var standalone = result.RootNodes[2];
        standalone.Name.Should().Be("Standalone Server");
        standalone.InheritsCredentials.Should().BeFalse(
            "absence of Inheritance attribute defaults to false");
    }

    // Test D: Inheritance="Username" only -> InheritsCredentials = true
    [Fact]
    public async Task ParseAsync_InheritanceWithUsernameTokenOnly_SetsInheritsCredentialsTrue()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
                <Node Name="UserOnly" Type="Connection" Protocol="RDP"
                      Hostname="user.local" Port="3389"
                      Inheritance="Username" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);
        var result = await _importer.ParseAsync(stream, Ct);

        result.RootNodes[0].InheritsCredentials.Should().BeTrue();
    }

    // Test E: Inheritance="Password" only -> InheritsCredentials = true
    [Fact]
    public async Task ParseAsync_InheritanceWithPasswordTokenOnly_SetsInheritsCredentialsTrue()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
                <Node Name="PwdOnly" Type="Connection" Protocol="RDP"
                      Hostname="pwd.local" Port="3389"
                      Inheritance="Password" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);
        var result = await _importer.ParseAsync(stream, Ct);

        result.RootNodes[0].InheritsCredentials.Should().BeTrue();
    }

    // Test F: Inheritance with unrelated tokens only -> InheritsCredentials = false
    [Fact]
    public async Task ParseAsync_InheritanceWithUnrelatedTokensOnly_SetsInheritsCredentialsFalse()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Connections Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
                <Node Name="OtherTokens" Type="Connection" Protocol="RDP"
                      Hostname="other.local" Port="3389"
                      Inheritance="Resolution,Colors" Description="" />
            </Connections>
            """;
        using var stream = ToStream(xml);
        var result = await _importer.ParseAsync(stream, Ct);

        result.RootNodes[0].InheritsCredentials.Should().BeFalse(
            "Resolution/Colors tokens do not affect credential inheritance");
    }
}
