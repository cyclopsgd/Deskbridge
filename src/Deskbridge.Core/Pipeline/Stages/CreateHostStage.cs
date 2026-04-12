using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Resolves the protocol host factory for the connection's protocol and stores the
/// created host on <see cref="ConnectionContext.Host"/> for downstream stages.
///
/// <para>Publishes <see cref="HostCreatedEvent"/> synchronously before returning so the
/// <c>ConnectionCoordinator</c> can mount the host's view into the WPF visual tree
/// (and force a layout pass) BEFORE the next stage — <c>ConnectStage</c> (Order=300) —
/// calls <see cref="IProtocolHost.ConnectAsync"/>. This is the siting-order requirement
/// documented in RDP-ACTIVEX-PITFALLS §1: AxHost's HWND is only realized once the
/// <c>WindowsFormsHost</c> is parented and laid out.</para>
/// </summary>
public sealed class CreateHostStage(IProtocolHostFactory factory, IEventBus bus) : IConnectionPipelineStage
{
    public string Name => "CreateHost";
    public int Order => 200;

    public Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        ctx.Host = factory.Create(ctx.Connection.Protocol);

        // Publish synchronously — WeakReferenceMessenger.Send dispatches inline, so by
        // the time Publish returns the coordinator has already (a) marshaled to STA and
        // (b) mounted the host into ViewportGrid with a forced UpdateLayout(). The next
        // stage (ConnectStage) can then safely touch the AxHost HWND.
        bus.Publish(new HostCreatedEvent(ctx.Connection, ctx.Host));

        return Task.FromResult(new PipelineResult(true));
    }
}
