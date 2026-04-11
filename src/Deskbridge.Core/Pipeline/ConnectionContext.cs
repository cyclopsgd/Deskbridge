using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Pipeline;

public class ConnectionContext
{
    public required ConnectionModel Connection { get; set; }

    /// <summary>
    /// Resolved credential password. Do not log or serialize this value.
    /// </summary>
    public string? ResolvedPassword { get; set; }

    public IProtocolHost? Host { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
    public CancellationToken CancellationToken { get; set; }
}
