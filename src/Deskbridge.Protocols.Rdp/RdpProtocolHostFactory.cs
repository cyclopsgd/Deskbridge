using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Resolves <see cref="Protocol.Rdp"/> to a fresh <see cref="RdpHostControl"/> instance.
/// Registered as <see cref="IProtocolHostFactory"/> singleton in DI (<c>App.xaml.cs</c>).
/// Throws <see cref="NotSupportedException"/> for SSH/VNC until v2 adds those hosts.
/// </summary>
public sealed class RdpProtocolHostFactory : IProtocolHostFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public RdpProtocolHostFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IProtocolHost Create(Protocol protocol, Guid connectionId)
    {
        if (protocol != Protocol.Rdp)
        {
            throw new NotSupportedException(
                $"Protocol '{protocol}' is not supported in Phase 4. Only Protocol.Rdp is implemented.");
        }

        return new RdpHostControl(_loggerFactory.CreateLogger<RdpHostControl>(), connectionId);
    }
}
