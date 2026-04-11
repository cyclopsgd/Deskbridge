using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Pipeline;

public class DisconnectContext
{
    public required ConnectionModel Connection { get; set; }
    public IProtocolHost? Host { get; set; }
    public DisconnectReason Reason { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
    public CancellationToken CancellationToken { get; set; }
}
