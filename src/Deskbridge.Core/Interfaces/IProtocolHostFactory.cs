using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Resolves an <see cref="IProtocolHost"/> implementation for a given <see cref="Protocol"/>.
/// <see cref="Pipeline.Stages.CreateHostStage"/> (Order=200) uses this to instantiate exactly
/// one host per connection attempt. Phase 4 supports <see cref="Protocol.Rdp"/>; Phase v2+ adds
/// SSH/VNC without changing this contract.
/// </summary>
public interface IProtocolHostFactory
{
    IProtocolHost Create(Protocol protocol);
}
