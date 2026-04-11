using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

public sealed class ConnectionQueryService : IConnectionQuery
{
    private readonly IConnectionStore? _store;
    private readonly List<ConnectionModel> _connections;

    public ConnectionQueryService()
    {
        _connections = [];
    }

    public ConnectionQueryService(IEnumerable<ConnectionModel> connections)
    {
        _connections = connections.ToList();
    }

    public ConnectionQueryService(IConnectionStore store)
    {
        _store = store;
        _connections = [];
    }

    private IReadOnlyList<ConnectionModel> CurrentConnections =>
        _store is not null ? _store.GetAll() : _connections.AsReadOnly();

    public IReadOnlyList<ConnectionModel> GetAll() => CurrentConnections;

    public IReadOnlyList<ConnectionModel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        var normalizedQuery = query.Trim().ToLowerInvariant();

        return CurrentConnections
            .Select(c => new { Connection = c, Score = CalculateScore(c, normalizedQuery) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Connection)
            .ToList();
    }

    public IReadOnlyList<ConnectionModel> GetByGroup(Guid groupId)
        => CurrentConnections.Where(c => c.GroupId == groupId).ToList();

    public IReadOnlyList<ConnectionModel> GetByTag(string tag)
        => CurrentConnections.Where(c => c.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<ConnectionModel> GetByFilter(ConnectionFilter filter)
    {
        var results = CurrentConnections.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var searchResults = Search(filter.SearchText);
            results = results.Where(c => searchResults.Contains(c));
        }
        if (filter.Tag is not null)
            results = results.Where(c => c.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase));
        if (filter.Protocol is not null)
            results = results.Where(c => c.Protocol == filter.Protocol);
        if (filter.GroupId is not null)
            results = results.Where(c => c.GroupId == filter.GroupId);

        return results.ToList();
    }

    public IReadOnlyList<ConnectionModel> GetRecent(int count = 10)
        => CurrentConnections.OrderByDescending(c => c.UpdatedAt).Take(count).ToList();

    private static int CalculateScore(ConnectionModel c, string query)
    {
        int score = 0;

        if (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 100;

        if (c.Hostname.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 80;

        if (c.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
            score += 60;

        if (score == 0)
        {
            if (IsSubsequence(query, c.Name.ToLowerInvariant()))
                score += 40;
            if (IsSubsequence(query, c.Hostname.ToLowerInvariant()))
                score += 30;
        }

        return score;
    }

    private static bool IsSubsequence(string query, string target)
    {
        int qi = 0;
        for (int ti = 0; ti < target.Length && qi < query.Length; ti++)
        {
            if (query[qi] == target[ti])
                qi++;
        }
        return qi == query.Length;
    }
}
