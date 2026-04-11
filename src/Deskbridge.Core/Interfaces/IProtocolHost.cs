using Deskbridge.Core.Pipeline;

namespace Deskbridge.Core.Interfaces;

public interface IProtocolHost : IDisposable
{
    Guid ConnectionId { get; }
    bool IsConnected { get; }
    Task ConnectAsync(ConnectionContext context);
    Task DisconnectAsync();
    event EventHandler<string>? ErrorOccurred;
}
