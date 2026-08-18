namespace HHDCTracker.Models;

public class Invoice
{
    public int InvoiceId { get; set; }
    public int? VoucherId { get; set; }         // nullable until resolved
    public int ChildId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int? ImportSessionId { get; set; }

    public DateTime InvoiceStart { get; set; }
    public DateTime InvoiceEnd { get; set; }
    public DateTime? PaymentDate { get; set; }
    public int AbsenceDays { get; set; }
    public int ClosureDays { get; set; }

    // Snapshotted rates
    public decimal? DailyVORate { get; set; }
    public decimal? DailyHHDCRate { get; set; }

    // Stored calculated fields
    public int? DaysBilled { get; set; }
    public decimal? VOExpectedTotal { get; set; }
    public decimal? HHDCExpectedTotal { get; set; }

    // Payments
    public decimal MDExcelsAmount { get; set; }
    public decimal ScholarshipAmount { get; set; }
    public decimal PaymentTotal { get; set; }

    // Stored discrepancy
    public decimal? VODiscrepancy { get; set; }
    public decimal? HHDCSurplus { get; set; }

    // Unresolved flag
    public bool IsUnresolved { get; set; }
    public string? RawVoucherNumber { get; set; }
    public string? RawChildName { get; set; }

    public bool HasTrueUpFlag { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int? ImportedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public int? LastModifiedByUserId { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Voucher? Voucher { get; set; }
    public Child? Child { get; set; }
    public ImportSession? ImportSession { get; set; }
    public ICollection<TrueUp> TrueUps { get; set; } = [];
    public ICollection<ManualAdjustment> ManualAdjustments { get; set; } = [];
}
