namespace HHDCTracker.Models;

public class User
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff";   // "Admin" | "Staff"
    public bool IsActive { get; set; } = true;
    public bool IsArchived { get; set; } = false;
    public int? LastUsedLocationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Location? LastUsedLocation { get; set; }
    public ICollection<UserLocation> UserLocations { get; set; } = [];
}
