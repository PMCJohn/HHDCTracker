namespace HHDCTracker.Models;

public class Child
{
    public int ChildId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public string ActiveVoucherSummary
    {
        get
        {
            var active = Vouchers.FirstOrDefault(v =>
                v.TerminationDate == null &&
                (v.PeriodEnd == null || v.PeriodEnd >= DateTime.Today) &&
                v.PeriodStart <= DateTime.Today);
            return active != null
                ? $"Voucher: {active.VoucherNumber}"
                : Vouchers.Any() ? "No active voucher" : "No vouchers";
        }
    }
    public DateTime? DateOfBirth { get; set; }
    public bool IsInfant { get; set; }
    public int LocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ArchivedAt { get; set; }
    public bool IsArchived => ArchivedAt.HasValue;
    public bool HasUnresolved { get; set; } = false;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public int? LastModifiedByUserId { get; set; }

    // Navigation
    public Location? Location { get; set; }
    public ICollection<ChildAlias> Aliases { get; set; } = [];
    public ICollection<Voucher> Vouchers { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<TrueUp> TrueUps { get; set; } = [];
    public ICollection<ManualAdjustment> ManualAdjustments { get; set; } = [];
}
