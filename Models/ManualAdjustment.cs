namespace HHDCTracker.Models;

public class ManualAdjustment
{
    public int AdjustmentId { get; set; }
    public int ChildId { get; set; }
    public int? VoucherId { get; set; }
    public int? InvoiceId { get; set; }
    public DateTime AdjustmentDate { get; set; }
    public decimal Amount { get; set; }
    public string ApplyTo { get; set; } = string.Empty;
        // "Balance" | "Credit" | "Balance from Credit"
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public int? LastModifiedByUserId { get; set; }

    // Navigation
    public Child? Child { get; set; }
    public Voucher? Voucher { get; set; }
    public Invoice? Invoice { get; set; }
}
