namespace HHDCTracker.Models;

public class Voucher
{
    public int VoucherId { get; set; }
    public int ChildId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string RateType { get; set; } = "Normal Rate";
    public DateTime PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public DateTime? TerminationDate { get; set; }

    // Standard rates
    public decimal VOPromisedWeekly { get; set; }
    public decimal HHDCChargeWeekly { get; set; }

    // Computed — not stored in DB, derived from weekly rates
    public decimal DailyVORate => VOPromisedWeekly / 5;
    public decimal DailyHHDCRate => HHDCChargeWeekly / 5;
    public decimal ExpectedWeeklyCopay => HHDCChargeWeekly - VOPromisedWeekly;

    // Summer rate window (optional)
    public DateTime? SummerRateStart { get; set; }
    public DateTime? SummerRateEnd { get; set; }
    public decimal? VOSummerWeekly { get; set; }
    public decimal? HHDCSummerWeekly { get; set; }

    public string? PCACodeLabel { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public int? LastModifiedByUserId { get; set; }

    // Navigation
    public Child? Child { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<TrueUp> TrueUps { get; set; } = [];

    // Helper: is this voucher currently active?
    public bool IsActive => TerminationDate == null
        && (PeriodEnd == null || PeriodEnd >= DateTime.Today);

    // Helper: get the correct daily VO rate for a given invoice date
    // (accounts for summer rate window)
    public decimal GetDailyVORateForDate(DateTime date)
    {
        if (SummerRateStart.HasValue && SummerRateEnd.HasValue &&
            VOSummerWeekly.HasValue &&
            date >= SummerRateStart.Value && date <= SummerRateEnd.Value)
            return VOSummerWeekly.Value / 5;
        return DailyVORate;
    }

    public decimal GetDailyHHDCRateForDate(DateTime date)
    {
        if (SummerRateStart.HasValue && SummerRateEnd.HasValue &&
            HHDCSummerWeekly.HasValue &&
            date >= SummerRateStart.Value && date <= SummerRateEnd.Value)
            return HHDCSummerWeekly.Value / 5;
        return DailyHHDCRate;
    }
}
