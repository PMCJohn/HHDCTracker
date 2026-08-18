namespace HHDCTracker.Models;

public class TrueUp
{
    public int TrueUpId { get; set; }
    public int VoucherId { get; set; }
    public int ChildId { get; set; }
    public int? ImportSessionId { get; set; }
    public int? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    public string TrueUpType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? FirstAPInvoiceNumber { get; set; }
    public string? SecondAPInvoiceNumber { get; set; }
    public string? APReconcilingMonth { get; set; }
    public int AdjustDays { get; set; }
    public decimal TrueUpAdjustAmount { get; set; }
    public decimal APAmount { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int? ImportedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public int? LastModifiedByUserId { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Voucher? Voucher { get; set; }
    public Child? Child { get; set; }
    public Invoice? Invoice { get; set; }
    public ImportSession? ImportSession { get; set; }
}
