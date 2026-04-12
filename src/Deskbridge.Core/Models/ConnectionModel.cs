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
}

public class DisplaySettings
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool SmartSizing { get; set; } = true;
}
