using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 22 (D-08): reverse-mapper. Serializes (connections, groups) to mRemoteNG
/// confCons.xml schema. Output round-trips with <see cref="MRemoteNGImporter.ParseAsync"/> —
/// the parser can re-read what this writer emits. Used by Phase 22's stress tests
/// to generate large fixtures at test time without committing 3MB XML to git.
///
/// Schema authority: tests/Deskbridge.Tests/Fixtures/sample-confcons.xml. The
/// only attribute the parser READS on the root is FullFileEncryption — must be
/// "false" (lowercase) or ParseAsync throws ImportException
/// (MRemoteNGImporter.cs:40).
///
/// MIG-03 boundary: Password is emitted as empty string — never a real value,
/// even though the parser ignores the attribute entirely.
///
/// Pattern source: ConnectionExporter (static class, GroupBy/ToDictionary
/// parent-id lookup, recursive WriteGroupNode/WriteConnectionNode helpers,
/// no DI dependencies — callable from any thread).
/// </summary>
public static class MRemoteNGXmlSerializer
{
    public static void Serialize(
        Stream output,
        IReadOnlyList<ConnectionModel> connections,
        IReadOnlyList<ConnectionGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            // RESEARCH P10: UTF-8 NO BOM — match sample-confcons.xml's encoding declaration.
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        };

        using var writer = XmlWriter.Create(output, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("Connections");

        // Cosmetic root attributes — mirror sample-confcons.xml so a real mRemoteNG
        // could open the file. Only FullFileEncryption is read by the parser.
        writer.WriteAttributeString("Name", "Connections");
        writer.WriteAttributeString("Export", "false");
        writer.WriteAttributeString("EncryptionEngine", "AES");
        writer.WriteAttributeString("BlockCipherMode", "GCM");
        writer.WriteAttributeString("KdfIterations", "1000");
        // CRITICAL — must be lowercase "false". Uppercase "True" causes
        // MRemoteNGImporter to throw ImportException ("file appears encrypted").
        writer.WriteAttributeString("FullFileEncryption", "false");
        writer.WriteAttributeString("Protected", "GiIJi/PiCE4=");
        writer.WriteAttributeString("ConfVersion", "2.6");

        // Build parent-id lookup tables (mirrors ConnectionExporter pattern).
        var groupsByParent = groups
            .GroupBy(g => GuidKey(g.ParentGroupId))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ToList());
        var connectionsByGroup = connections
            .GroupBy(c => GuidKey(c.GroupId))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ToList());

        var rootKey = GuidKey(null);

        // Top-level groups
        if (groupsByParent.TryGetValue(rootKey, out var rootGroups))
        {
            foreach (var g in rootGroups)
                WriteGroupNode(writer, g, groupsByParent, connectionsByGroup);
        }

        // Top-level connections (no group)
        if (connectionsByGroup.TryGetValue(rootKey, out var rootConns))
        {
            foreach (var c in rootConns)
                WriteConnectionNode(writer, c);
        }

        writer.WriteEndElement();   // </Connections>
        writer.WriteEndDocument();
    }

    private static void WriteGroupNode(
        XmlWriter w,
        ConnectionGroup group,
        Dictionary<string, List<ConnectionGroup>> groupsByParent,
        Dictionary<string, List<ConnectionModel>> connectionsByGroup)
    {
        w.WriteStartElement("Node");
        w.WriteAttributeString("Name", group.Name);
        w.WriteAttributeString("Type", "Container");           // Case-sensitive
        w.WriteAttributeString("Expanded", "True");
        w.WriteAttributeString("Protocol", "RDP");             // Cosmetic on Container
        w.WriteAttributeString("Hostname", string.Empty);
        w.WriteAttributeString("Port", "3389");
        w.WriteAttributeString("Username", string.Empty);
        w.WriteAttributeString("Domain", string.Empty);
        w.WriteAttributeString("Password", string.Empty);      // MIG-03 — never real
        w.WriteAttributeString("Description", string.Empty);
        w.WriteAttributeString("Icon", "Folder");
        w.WriteAttributeString("Panel", "General");

        var key = GuidKey(group.Id);

        // Recurse into sub-groups
        if (groupsByParent.TryGetValue(key, out var subGroups))
        {
            foreach (var sub in subGroups)
                WriteGroupNode(w, sub, groupsByParent, connectionsByGroup);
        }

        // Connections in this group
        if (connectionsByGroup.TryGetValue(key, out var conns))
        {
            foreach (var conn in conns)
                WriteConnectionNode(w, conn);
        }

        w.WriteEndElement();
    }

    private static void WriteConnectionNode(XmlWriter w, ConnectionModel c)
    {
        w.WriteStartElement("Node");
        w.WriteAttributeString("Name", c.Name);
        w.WriteAttributeString("Type", "Connection");          // Case-sensitive
        w.WriteAttributeString("Protocol", ProtocolToWire(c.Protocol));
        w.WriteAttributeString("Hostname", c.Hostname ?? string.Empty);
        w.WriteAttributeString("Port", c.Port.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("Username", c.Username ?? string.Empty);
        w.WriteAttributeString("Domain", c.Domain ?? string.Empty);
        w.WriteAttributeString("Password", string.Empty);      // MIG-03
        w.WriteAttributeString("Description", c.Notes ?? string.Empty);
        w.WriteAttributeString("Icon", "mRemoteNG");
        w.WriteAttributeString("Panel", "General");
        w.WriteEndElement();
    }

    private static string ProtocolToWire(Protocol p) => p switch
    {
        Protocol.Rdp => "RDP",
        Protocol.Ssh => "SSH2",
        Protocol.Vnc => "VNC",
        _ => "RDP",
    };

    private static string GuidKey(Guid? id) => id?.ToString() ?? string.Empty;
}
