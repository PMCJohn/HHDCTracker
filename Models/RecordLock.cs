namespace HHDCTracker.Models;

public class RecordLock
{
    public int LockId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public int LockedByUserId { get; set; }
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    // Navigation
    public User? LockedByUser { get; set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}
