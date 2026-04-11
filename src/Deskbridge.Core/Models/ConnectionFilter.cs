namespace Deskbridge.Core.Models;

public class ConnectionFilter
{
    public string? SearchText { get; set; }
    public string? Tag { get; set; }
    public Protocol? Protocol { get; set; }
    public Guid? GroupId { get; set; }
    public bool? IsConnected { get; set; }
}
