namespace HHDCTracker.Models;

public class Location
{
    public int LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Child> Children { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}
