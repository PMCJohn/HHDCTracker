using HHDCTracker.Data;
using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Services;

/// <summary>
/// Manages record locks so two users can't edit the same record simultaneously.
/// Locks expire after 30 minutes of inactivity.
/// </summary>
public class LockService
{
    private readonly AppDbContext _db;
    private readonly int _currentUserId;
    private const int LockMinutes = 30;

    public LockService(AppDbContext db, int currentUserId)
    {
        _db = db;
        _currentUserId = currentUserId;
    }

    /// <summary>
    /// Tries to acquire a lock. Returns null on success, or the name of the
    /// user currently holding the lock if it's taken.
    /// </summary>
    public async Task<string?> TryAcquireAsync(string tableName, int recordId)
    {
        // Clear any expired locks first
        var expired = await _db.RecordLocks
            .Where(l => l.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
        _db.RecordLocks.RemoveRange(expired);
        await _db.SaveChangesAsync();

        var existing = await _db.RecordLocks
            .Include(l => l.LockedByUser)
            .FirstOrDefaultAsync(l => l.TableName == tableName && l.RecordId == recordId);

        if (existing != null)
        {
            // Someone else holds this lock
            if (existing.LockedByUserId != _currentUserId)
                return existing.LockedByUser?.DisplayName ?? "Another user";

            // We hold it — refresh the expiry
            existing.ExpiresAt = DateTime.UtcNow.AddMinutes(LockMinutes);
            await _db.SaveChangesAsync();
            return null;
        }

        _db.RecordLocks.Add(new RecordLock
        {
            TableName = tableName,
            RecordId = recordId,
            LockedByUserId = _currentUserId,
            LockedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(LockMinutes)
        });
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task ReleaseAsync(string tableName, int recordId)
    {
        var lock_ = await _db.RecordLocks
            .FirstOrDefaultAsync(l => l.TableName == tableName
                && l.RecordId == recordId
                && l.LockedByUserId == _currentUserId);
        if (lock_ != null)
        {
            _db.RecordLocks.Remove(lock_);
            await _db.SaveChangesAsync();
        }
    }

    public async Task RefreshAsync(string tableName, int recordId)
    {
        var lock_ = await _db.RecordLocks
            .FirstOrDefaultAsync(l => l.TableName == tableName
                && l.RecordId == recordId
                && l.LockedByUserId == _currentUserId);
        if (lock_ != null)
        {
            lock_.ExpiresAt = DateTime.UtcNow.AddMinutes(LockMinutes);
            await _db.SaveChangesAsync();
        }
    }
}
