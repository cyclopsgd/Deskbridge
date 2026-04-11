using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface IConnectionStore
{
    IReadOnlyList<ConnectionModel> GetAll();
    ConnectionModel? GetById(Guid id);
    void Save(ConnectionModel connection);
    void Delete(Guid id);
    IReadOnlyList<ConnectionGroup> GetGroups();
    ConnectionGroup? GetGroupById(Guid id);
}
