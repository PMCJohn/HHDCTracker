namespace HHDCTracker.Helpers;

public static class NetworkDaysHelper
{
    /// <summary>
    /// Count business days between two dates inclusive. No closure day deduction —
    /// MSDE uses straight Mon-Fri counts.
    /// </summary>
    public static int Calculate(DateTime start, DateTime end)
    {
        if (end < start) return 0;
        int count = 0;
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
        return count;
    }

    /// <summary>
    /// Business days in a full calendar month — used for MSDE monthly expected calculation.
    /// </summary>
    public static int MonthBusinessDays(int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return Calculate(start, end);
    }

    /// <summary>
    /// Business days a voucher was active within a given calendar month.
    /// Clamps to month boundary for partial starts/ends.
    /// </summary>
    public static int VoucherBusinessDaysInMonth(
        DateTime voucherStart, DateTime? voucherEnd, int year, int month)
    {
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var effectiveStart = voucherStart > monthStart ? voucherStart : monthStart;
        var effectiveEnd = voucherEnd.HasValue && voucherEnd.Value < monthEnd
            ? voucherEnd.Value : monthEnd;
        if (effectiveEnd < effectiveStart) return 0;
        return Calculate(effectiveStart, effectiveEnd);
    }
}
