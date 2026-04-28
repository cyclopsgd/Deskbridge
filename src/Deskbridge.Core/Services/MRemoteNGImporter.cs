using System.IO;
using System.Xml;
using System.Xml.Linq;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

public class MRemoteNGImporter : IConnectionImporter
{
    public string SourceName => "mRemoteNG";
    public string FileFilter => "mRemoteNG Config (*.xml)|*.xml";

    public Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit
            };
            using var reader = XmlReader.Create(stream, settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ImportException(
                "The selected file is not a valid mRemoteNG configuration file.", ex);
        }

        var root = doc.Root;
        if (root is null)
        {
            throw new ImportException(
                "The selected file is not a valid mRemoteNG configuration file.");
        }

        // Detect encrypted files (case-sensitive "True" per mRemoteNG convention)
        if (root.Attribute("FullFileEncryption")?.Value == "True")
        {
            throw new ImportException(
                "This file appears to be encrypted. Please export an unencrypted version from mRemoteNG (File > Export).");
        }

        ct.ThrowIfCancellationRequested();

        var nodes = new List<ImportedNode>();
        int totalConnections = 0;
        int totalFolders = 0;

        foreach (var el in root.Elements("Node"))
        {
            var node = ParseNode(el, ref totalConnections, ref totalFolders);
            nodes.Add(node);
        }

        var result = new ImportResult(nodes, totalConnections, totalFolders);
        return Task.FromResult(result);
    }

    private static ImportedNode ParseNode(XElement el, ref int totalConnections, ref int totalFolders)
    {
        var typeAttr = el.Attribute("Type")?.Value;
        var nodeType = typeAttr == "Container"
            ? ImportNodeType.Container
            : ImportNodeType.Connection;

        if (nodeType == ImportNodeType.Container)
            totalFolders++;
        else
            totalConnections++;

        var protocol = MapProtocol(el.Attribute("Protocol")?.Value);
        var port = int.TryParse(el.Attribute("Port")?.Value, out var p) ? p : 3389;

        var name = SanitizeName(el.Attribute("Name")?.Value ?? string.Empty);
        var hostname = NullIfEmpty(el.Attribute("Hostname")?.Value);
        var username = NullIfEmpty(el.Attribute("Username")?.Value);
        var domain = NullIfEmpty(el.Attribute("Domain")?.Value);
        var description = NullIfEmpty(el.Attribute("Description")?.Value);
        var inheritsCredentials = ParseInherits(el.Attribute("Inheritance")?.Value);

        // Password attribute is EXPLICITLY SKIPPED -- never read, stored, or logged (MIG-03)

        var children = new List<ImportedNode>();
        foreach (var child in el.Elements("Node"))
        {
            children.Add(ParseNode(child, ref totalConnections, ref totalFolders));
        }

        return new ImportedNode(
            name, nodeType, hostname, port,
            username, domain, protocol, description,
            inheritsCredentials,
            children);
    }

    /// <summary>
    /// Parse the mRemoteNG <c>Inheritance</c> attribute (comma-separated tokens).
    /// Returns true if either <c>Username</c> or <c>Password</c> appears in the token list.
    /// Empty/missing/<c>None</c> -> false.
    /// </summary>
    private static bool ParseInherits(string? inheritance)
    {
        if (string.IsNullOrWhiteSpace(inheritance)) return false;

        var tokens = inheritance.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in tokens)
        {
            if (t.Equals("Username", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("Password", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static Protocol MapProtocol(string? value) => value switch
    {
        "RDP" => Protocol.Rdp,
        "SSH2" or "SSH1" => Protocol.Ssh,
        "VNC" => Protocol.Vnc,
        _ => Protocol.Rdp
    };

    /// <summary>
    /// Sanitize imported names: strip path separators and control characters
    /// to prevent path traversal or UI injection (T-07-10).
    /// </summary>
    private static string SanitizeName(string name)
    {
        var chars = name.ToCharArray();
        var sb = new System.Text.StringBuilder(chars.Length);
        foreach (var c in chars)
        {
            // Skip path separators
            if (c is '/' or '\\')
                continue;

            // Skip control characters (< 0x20) except tab and newline
            if (c < '\x20' && c is not ('\t' or '\n' or '\r'))
                continue;

            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
