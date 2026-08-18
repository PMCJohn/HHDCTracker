using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Data;

public class AppDbContext : DbContext
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<ChildAlias> ChildAliases => Set<ChildAlias>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<TrueUp> TrueUps => Set<TrueUp>();
    public DbSet<ManualAdjustment> ManualAdjustments => Set<ManualAdjustment>();
    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();
    public DbSet<RecordLock> RecordLocks => Set<RecordLock>();
    public DbSet<UnresolvedTrueUp> UnresolvedTrueUps => Set<UnresolvedTrueUp>();

    private readonly string _dbPath;
    public AppDbContext(string dbPath) { _dbPath = dbPath; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath};Pooling=False;");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Location>(e => {
            e.HasKey(x => x.LocationId);
            e.Property(x => x.Name).IsRequired();
        });
        model.Entity<User>(e => {
            e.HasKey(x => x.UserId);
            e.Property(x => x.DisplayName).IsRequired();
            e.Property(x => x.Role).HasDefaultValue("Staff");
            e.Ignore(x => x.LastUsedLocation);
            e.HasMany(x => x.UserLocations).WithOne(x => x.User)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<UserLocation>(e => {
            e.HasKey(x => x.UserLocationId);
            e.HasIndex(x => new { x.UserId, x.LocationId }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.UserLocations)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location).WithMany()
             .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Child>(e => {
            e.HasKey(x => x.ChildId);
            e.Property(x => x.FirstName).IsRequired();
            e.Property(x => x.LastName).IsRequired();
            e.Ignore(x => x.FullName);
            e.Ignore(x => x.IsArchived);
            e.Ignore(x => x.ActiveVoucherSummary);
            e.HasOne(x => x.Location).WithMany(x => x.Children)
             .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ChildAlias>(e => {
            e.HasKey(x => x.AliasId);
            e.HasIndex(x => new { x.ChildId, x.AliasName }).IsUnique();
            e.HasOne(x => x.Child).WithMany(x => x.Aliases)
             .HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Voucher>(e => {
            e.HasKey(x => x.VoucherId);
            e.Ignore(x => x.DailyVORate);
            e.Ignore(x => x.DailyHHDCRate);
            e.Ignore(x => x.ExpectedWeeklyCopay);
            e.Property(x => x.VOPromisedWeekly).HasColumnType("TEXT");
            e.Property(x => x.HHDCChargeWeekly).HasColumnType("TEXT");
            e.Property(x => x.VOSummerWeekly).HasColumnType("TEXT");
            e.Property(x => x.HHDCSummerWeekly).HasColumnType("TEXT");
            e.HasOne(x => x.Child).WithMany(x => x.Vouchers)
             .HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Invoice>(e => {
            e.HasKey(x => x.InvoiceId);
            foreach (var p in new[] { "MDExcelsAmount","ScholarshipAmount","PaymentTotal",
                "VOExpectedTotal","HHDCExpectedTotal","VODiscrepancy","HHDCSurplus",
                "DailyVORate","DailyHHDCRate" })
                e.Property(p).HasColumnType("TEXT");
            e.HasOne(x => x.Voucher).WithMany(x => x.Invoices)
             .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Child).WithMany(x => x.Invoices)
             .HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ImportSession).WithMany(x => x.Invoices)
             .HasForeignKey(x => x.ImportSessionId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<TrueUp>(e => {
            e.HasKey(x => x.TrueUpId);
            e.Property(x => x.TrueUpAdjustAmount).HasColumnType("TEXT");
            e.Property(x => x.APAmount).HasColumnType("TEXT");
            e.HasOne(x => x.Voucher).WithMany(x => x.TrueUps)
             .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Child).WithMany(x => x.TrueUps)
             .HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Invoice).WithMany(x => x.TrueUps)
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ImportSession).WithMany(x => x.TrueUps)
             .HasForeignKey(x => x.ImportSessionId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<ManualAdjustment>(e => {
            e.HasKey(x => x.AdjustmentId);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.HasOne(x => x.Child).WithMany(x => x.ManualAdjustments)
             .HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Voucher).WithMany()
             .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Invoice).WithMany(x => x.ManualAdjustments)
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<ImportSession>(e => e.HasKey(x => x.ImportSessionId));
        model.Entity<UnresolvedTrueUp>(e => {
            e.HasKey(x => x.UnresolvedTrueUpId);
            e.Property(x => x.TrueUpAdjustAmount).HasColumnType("TEXT");
            e.Property(x => x.APAmount).HasColumnType("TEXT");
            e.HasOne(x => x.ImportSession).WithMany()
             .HasForeignKey(x => x.ImportSessionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ResolvedChild).WithMany()
             .HasForeignKey(x => x.ResolvedChildId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<RecordLock>(e => {
            e.HasKey(x => x.LockId);
            e.HasIndex(x => new { x.TableName, x.RecordId }).IsUnique();
            e.Ignore(x => x.IsExpired);
            e.HasOne(x => x.LockedByUser).WithMany()
             .HasForeignKey(x => x.LockedByUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
