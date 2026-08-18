namespace HHDCTracker.Models;

/// <summary>
/// Stores true-up rows from MSDE that couldn't be matched to a child or voucher.
/// Lives here until manually resolved via the Unresolved page.
/// </summary>
public class UnresolvedTrueUp
{
    public int UnresolvedTrueUpId { get; set; }
    public int? ImportSessionId { get; set; }

    // Raw data from MSDE
    public string RawVoucherNumber { get; set; } = string.Empty;
    public string RawChildName { get; set; } = string.Empty;
    public string TrueUpType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? FirstAPInvoiceNumber { get; set; }
    public string? SecondAPInvoiceNumber { get; set; }
    public string? APReconcilingMonth { get; set; }
    public int AdjustDays { get; set; }
    public decimal TrueUpAdjustAmount { get; set; }
    public decimal APAmount { get; set; }

    // Resolution state
    public bool IsResolved { get; set; } = false;
    public int? ResolvedChildId { get; set; }
    public int? ResolvedVoucherId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }

    public int LocationId { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int? ImportedByUserId { get; set; }

    // Navigation
    public ImportSession? ImportSession { get; set; }
    public Child? ResolvedChild { get; set; }
}
