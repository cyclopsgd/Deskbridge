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
    /// Server authentication level. 0 = no authentication required (accepts any cert -- default for
    /// seamless connection experience); 1 = must authenticate; 2 = warn on auth failure. Default 0
    /// to skip certificate prompts. Users can override per-connection if needed.
    /// </summary>
    public uint AuthenticationLevel { get; set; } = 0;

    /// <summary>
    /// Deep copy of this connection. Used by transactional bulk edit (audit W1): edits are applied
    /// to clones so a persistence failure never leaves the store's live backing objects mutated.
    /// Value-typed and string properties are copied by <see cref="object.MemberwiseClone"/>; the
    /// mutable reference members (<see cref="Tags"/>, <see cref="DisplaySettings"/>) are deep-copied
    /// so the clone shares no mutable state with the original.
    /// </summary>
    public ConnectionModel Clone()
    {
        var clone = (ConnectionModel)MemberwiseClone();
        clone.Tags = [.. Tags];
        clone.DisplaySettings = DisplaySettings is null
            ? null
            : new DisplaySettings
            {
                Width = DisplaySettings.Width,
                Height = DisplaySettings.Height,
                SmartSizing = DisplaySettings.SmartSizing,
            };
        return clone;
    }
}

public class DisplaySettings
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool SmartSizing { get; set; } = true;
}
