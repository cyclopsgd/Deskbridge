namespace Deskbridge.Core.Models;

public class ConnectionModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string? Username { get; set; }
    public string? Domain { get; set; }
    public Protocol Protocol { get; set; } = Protocol.Rdp;
    public Guid? GroupId { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = [];
    public CredentialMode CredentialMode { get; set; } = CredentialMode.Inherit;
    public DisplaySettings? DisplaySettings { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the most recent successful connect. Nullable: older connections and
    /// connections that have never been opened remain <c>null</c>. Populated by
    /// <c>UpdateRecentsStage</c> (Order=400) after <c>ConnectionEstablishedEvent</c>.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// CredSSP / NLA support. Default true for Windows RDP servers; set false for xrdp and other
    /// non-CredSSP servers (e.g. non-Windows RDP implementations). When true with a server that
    /// doesn't support CredSSP, the client stalls during negotiation.
    /// </summary>
    public bool EnableCredSspSupport { get; set; } = true;

    /// <summary>
    /// Server authentication level. 0 = no authentication required (accepts any cert, useful for
    /// self-signed xrdp setups); 1 = must authenticate; 2 = warn on auth failure. Default 2 for
    /// production Windows targets; set 0 for xrdp / self-signed certificate scenarios.
    /// </summary>
    public uint AuthenticationLevel { get; set; } = 2;
}

public class DisplaySettings
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool SmartSizing { get; set; } = true;
}
