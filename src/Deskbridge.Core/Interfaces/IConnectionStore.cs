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
    void SaveGroup(ConnectionGroup group);
    void DeleteGroup(Guid groupId);
    void DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds);

    /// <summary>
    /// Atomically upserts <paramref name="connections"/> and <paramref name="groups"/> in a single file write.
    /// PRECONDITION: the incoming items MUST be caller-owned clones or fresh objects — NEVER the live
    /// references returned by <see cref="GetById"/>/<see cref="GetGroupById"/>. The rollback snapshot is
    /// shallow (it copies the list containers, not the elements), and the connection upsert stamps
    /// <c>UpdatedAt</c> on the incoming objects, so passing mutated live references defeats the
    /// all-or-nothing rollback guarantee and mutates in-memory state even on a failed write.
    /// </summary>
    void SaveBatch(IEnumerable<ConnectionModel> connections, IEnumerable<ConnectionGroup> groups);
    void Load();
    Task LoadAsync();
}
