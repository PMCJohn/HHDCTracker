using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
        await SeedDefaultDataAsync(db);
    }

    private static async Task SeedDefaultDataAsync(AppDbContext db)
    {
        if (!db.Locations.Any())
        {
            db.Locations.Add(new Location { Name = "Main Location" });
            await db.SaveChangesAsync();
        }

        if (!db.Users.Any())
        {
            var loc = db.Locations.First();
            var admin = new User { DisplayName = "Admin", Role = "Admin" };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            db.UserLocations.Add(new UserLocation
            {
                UserId = admin.UserId,
                LocationId = loc.LocationId
            });
            await db.SaveChangesAsync();
        }
    }
}
