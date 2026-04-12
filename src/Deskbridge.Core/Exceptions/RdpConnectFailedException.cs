namespace Deskbridge.Core.Exceptions;

/// <summary>
/// Thrown by <c>RdpHostControl</c> when an RDP connect attempt fails with a disconnect reason
/// code (via <c>OnDisconnected</c> firing during connect phase). <see cref="ConnectStage"/>
/// catches this and publishes <see cref="Events.ConnectionFailedEvent"/> with the human reason.
/// </summary>
public sealed class RdpConnectFailedException : Exception
{
    /// <summary>RDP disconnect reason code (see RDP-ACTIVEX-PITFALLS §7).</summary>
    public int DiscReason { get; }

    /// <summary>Human-readable classifier output from <c>DisconnectReasonClassifier.Describe</c>.</summary>
    public string HumanReason { get; }

    public RdpConnectFailedException(int discReason, string humanReason) : base(humanReason)
    {
        DiscReason = discReason;
        HumanReason = humanReason;
    }
}
