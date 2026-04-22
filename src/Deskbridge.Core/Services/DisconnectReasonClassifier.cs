namespace Deskbridge.Core.Services;

/// <summary>
/// Categorization of RDP disconnect-reason codes per RDP-ACTIVEX-PITFALLS §7. Used by
/// <see cref="DisconnectReasonClassifier.ShouldAutoRetry"/> to enforce the D-06 skip
/// list — authentication and licensing failures go straight to the manual overlay
/// (no backoff loop is meaningful for credential errors).
/// </summary>
public enum DisconnectCategory
{
    /// <summary>Code 2 — app-initiated disconnect. Never auto-retry.</summary>
    UserInitiated,
    /// <summary>Code 3 — server terminated the session. Retry with overlay.</summary>
    ServerInitiated,
    /// <summary>Codes 264, 516, 772, 1028, 2308 — transport failure. Retry with overlay.</summary>
    NetworkLost,
    /// <summary>Codes 260, 520 — name-resolution failure. Retry with overlay.</summary>
    DnsFailure,
    /// <summary>
    /// Codes 2055, 2567, 2823, 3335, 3591, 3847 — credential / policy failures.
    /// D-06: SKIP auto-retry, show manual overlay immediately.
    /// </summary>
    Authentication,
    /// <summary>Codes 2056, 2312 — licensing failures. D-06: SKIP auto-retry.</summary>
    Licensing,
    /// <summary>Code 3334 — protocol error. Retry with overlay (may be transient).</summary>
    Protocol,
    /// <summary>Code 1 -- user logged off from within the remote session. Close the tab.</summary>
    Logoff,
    /// <summary>Any code not in the documented table. Default retry behaviour.</summary>
    Unknown,
}

/// <summary>
/// Disconnect-reason classifier + describer. Protocol-agnostic per D-10 — the
/// <see cref="Describe"/> method takes a delegate so Core never references AxMSTSCLib.
///
/// <para>Plan 04-03 expands the Plan 04-02 stub with the full <see cref="DisconnectCategory"/>
/// enum, <see cref="Classify"/>, and <see cref="ShouldAutoRetry"/> (D-06 skip list).</para>
///
/// <para><b>Security:</b> the returned <see cref="Describe"/> string is safe to log —
/// it is sourced from the RDP ActiveX's <c>GetErrorDescription</c> (user-visible, no
/// credential content) or a generic fallback. Never contains password material.</para>
/// </summary>
public static class DisconnectReasonClassifier
{
    private static readonly HashSet<int> UserInitiatedCodes = [2];
    private static readonly HashSet<int> LogoffCodes = [1];
    private static readonly HashSet<int> ServerInitiatedCodes = [3];
    private static readonly HashSet<int> NetworkLostCodes = [264, 516, 772, 1028, 2308];
    private static readonly HashSet<int> DnsFailureCodes = [260, 520];
    private static readonly HashSet<int> AuthenticationCodes = [2055, 2567, 2823, 3335, 3591, 3847];
    private static readonly HashSet<int> LicensingCodes = [2056, 2312];
    private static readonly HashSet<int> ProtocolCodes = [3334];

    /// <summary>
    /// Maps a disconnect-reason code to its category. Codes not in the documented
    /// table fall through to <see cref="DisconnectCategory.Unknown"/>.
    /// </summary>
    public static DisconnectCategory Classify(int discReason) => discReason switch
    {
        _ when LogoffCodes.Contains(discReason) => DisconnectCategory.Logoff,
        _ when UserInitiatedCodes.Contains(discReason) => DisconnectCategory.UserInitiated,
        _ when ServerInitiatedCodes.Contains(discReason) => DisconnectCategory.ServerInitiated,
        _ when NetworkLostCodes.Contains(discReason) => DisconnectCategory.NetworkLost,
        _ when DnsFailureCodes.Contains(discReason) => DisconnectCategory.DnsFailure,
        _ when AuthenticationCodes.Contains(discReason) => DisconnectCategory.Authentication,
        _ when LicensingCodes.Contains(discReason) => DisconnectCategory.Licensing,
        _ when ProtocolCodes.Contains(discReason) => DisconnectCategory.Protocol,
        _ => DisconnectCategory.Unknown,
    };

    /// <summary>
    /// D-06 skip gate. Returns <c>true</c> for categories whose transient failure
    /// can plausibly clear with a retry (network, DNS, server, protocol, unknown).
    /// Returns <c>false</c> for user-initiated, authentication, and licensing —
    /// those require user intervention so the manual overlay shows immediately.
    /// </summary>
    public static bool ShouldAutoRetry(DisconnectCategory cat) => cat is
        DisconnectCategory.ServerInitiated or
        DisconnectCategory.NetworkLost or
        DisconnectCategory.DnsFailure or
        DisconnectCategory.Protocol or
        DisconnectCategory.Unknown;

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
