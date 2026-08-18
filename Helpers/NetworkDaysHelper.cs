namespace HHDCTracker.Helpers;

/// <summary>
/// Calculates business days between two dates (Mon-Fri only),
/// matching Excel's NETWORKDAYS function behavior.
/// </summary>
public static class NetworkDaysHelper
{
    public static int Calculate(DateTime start, DateTime end, int closureDays = 0)
    {
        if (end < start) return 0;

        int count = 0;
        for (DateTime d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
        }
        return Math.Max(0, count - closureDays);
    }
}
