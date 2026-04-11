using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface IConnectionQuery
{
    IReadOnlyList<ConnectionModel> GetAll();
    IReadOnlyList<ConnectionModel> Search(string query);
    IReadOnlyList<ConnectionModel> GetByGroup(Guid groupId);
    IReadOnlyList<ConnectionModel> GetByTag(string tag);
    IReadOnlyList<ConnectionModel> GetByFilter(ConnectionFilter filter);
    IReadOnlyList<ConnectionModel> GetRecent(int count = 10);
}
