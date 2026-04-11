namespace Deskbridge.Core.Models;

public class ConnectionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentGroupId { get; set; }
    public int SortOrder { get; set; }
}
