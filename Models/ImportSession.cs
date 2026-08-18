namespace HHDCTracker.Models;

public class ImportSession
{
    public int ImportSessionId { get; set; }
    public string ImportType { get; set; } = string.Empty; // "Payment" | "TrueUp"
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int? ImportedByUserId { get; set; }
    public int? LocationId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceStart { get; set; }
    public DateTime? InvoiceEnd { get; set; }
    public DateTime? PaymentDate { get; set; }
    public int ClosureDays { get; set; }
    public int RowsImported { get; set; }
    public int RowsSkipped { get; set; }
    public int RowsUnresolved { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<TrueUp> TrueUps { get; set; } = [];
}
