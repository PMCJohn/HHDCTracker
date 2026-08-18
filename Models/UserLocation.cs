namespace HHDCTracker.Models;

public class UserLocation
{
    public int UserLocationId { get; set; }
    public int UserId { get; set; }
    public int LocationId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
    public Location? Location { get; set; }
}
