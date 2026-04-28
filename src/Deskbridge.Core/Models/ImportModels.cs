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
    bool InheritsCredentials,
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
