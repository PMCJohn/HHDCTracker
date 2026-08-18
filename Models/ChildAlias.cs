namespace HHDCTracker.Models;

public class ChildAlias
{
    public int AliasId { get; set; }
    public int ChildId { get; set; }
    public string AliasName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    // Navigation
    public Child? Child { get; set; }
}
