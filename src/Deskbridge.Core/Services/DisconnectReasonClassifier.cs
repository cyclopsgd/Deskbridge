namespace Deskbridge.Core.Services;

/// <summary>
/// Minimal stub — Plan 04-03 fills in the full <c>DisconnectCategory</c> enum, the
/// <c>Classify</c> switch over the 19 canonical disconnect-reason codes, and
/// <c>ShouldAutoRetry</c>. Phase 4 Plan 02 only needs <see cref="Describe"/> so the
/// production <c>RdpHostControl</c> can emit a human string from <c>OnDisconnected</c>
/// into <c>RdpConnectFailedException.HumanReason</c>.
///
/// <para>
/// Protocol-agnostic: the <paramref name="getErrorDescription"/> delegate lets the caller
/// supply <c>AxMsRdpClient9.GetErrorDescription</c> without Core referencing AxMSTSCLib (D-10).
/// </para>
/// </summary>
public static class DisconnectReasonClassifier
{
    /// <summary>
    /// Returns a safe human-readable string for a disconnect-reason / extended-reason pair.
    /// If <paramref name="getErrorDescription"/> is provided and returns a non-empty string,
    /// that wins; otherwise a generic fallback is returned. Never contains credential material.
    /// </summary>
    public static string Describe(int discReason, int extendedReason, Func<uint, uint, string>? getErrorDescription = null)
    {
        if (getErrorDescription is not null)
        {
            try
            {
                var desc = getErrorDescription((uint)discReason, (uint)extendedReason);
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    return desc;
                }
            }
            catch
            {
                // COM call may throw mid-teardown. Fall through to fallback text.
            }
        }
        return $"Disconnect reason {discReason} (extended {extendedReason})";
    }
}
