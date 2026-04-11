using System.Net;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface ICredentialService
{
    NetworkCredential? GetForConnection(ConnectionModel connection);
    void StoreForConnection(ConnectionModel connection, string username, string? domain, string password);
    void DeleteForConnection(ConnectionModel connection);
    NetworkCredential? GetForGroup(Guid groupId);
    void StoreForGroup(Guid groupId, string username, string? domain, string password);
    void DeleteForGroup(Guid groupId);
    NetworkCredential? ResolveInherited(ConnectionModel connection, IConnectionStore connectionStore);
    bool HasGroupCredentials(Guid groupId);
}
