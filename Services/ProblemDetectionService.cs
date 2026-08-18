using HHDCTracker.Data;
using HHDCTracker.Helpers;
using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Services;

public class ProblemDetectionService
{
    private readonly AppDbContext _db;
    private readonly int _locationId;
    private const int GapThresholdDays = 20;
    private const double LowPaymentThreshold = 0.95;
    private const int MinInvoicesForMedian = 3;

    public ProblemDetectionService(AppDbContext db, int locationId)
    {
        _db = db;
        _locationId = locationId;
    }

    public record UnderpaymentProblem(
        Invoice Invoice, Child Child, Voucher Voucher,
        decimal VOShortfall, decimal TrueUpApplied, decimal ManualApplied,
        decimal Remaining, string IssueType, bool IsResolved);

    public record PaymentGapProblem(
        Child Child, Voucher Voucher,
        Invoice PreviousInvoice, Invoice NextInvoice,
        int GapDays, string Description);

    public record CoverageGapProblem(
        Child Child, Voucher? EndingVoucher, Voucher? StartingVoucher,
        DateTime GapStart, DateTime GapEnd,
        int GapDays, decimal ParentLiabilityAmount, string Description);

    public record LowPaymentProblem(
        Invoice Invoice, Child Child, Voucher Voucher,
        decimal MedianPayment, decimal ActualPayment,
        decimal PercentOfMedian, string Description);

    public record ProblemSummary(
        List<UnderpaymentProblem> Underpayments,
        List<PaymentGapProblem> PaymentGaps,
        List<CoverageGapProblem> CoverageGaps,
        List<LowPaymentProblem> LowPayments);

    public async Task<ProblemSummary> DetectAllProblemsAsync()
    {
        ProgressService.Report(0, "Loading children...");
        var children = await DbRetryService.ExecuteAsync(() =>
            _db.Children.AsNoTracking()
                .Include(c => c.Vouchers)
                .Where(c => c.LocationId == _locationId && !c.ArchivedAt.HasValue)
                .ToListAsync());

        ProgressService.Report(10, "Loading invoices...");
        var allInvoices = await DbRetryService.ExecuteAsync(() =>
            _db.Invoices.AsNoTracking()
                .Where(i => !i.IsUnresolved && i.VoucherId != null)
                .OrderBy(i => i.InvoiceStart)
                .ToListAsync());

        ProgressService.Report(20, "Loading true-ups and adjustments...");
        var allTrueUps = await DbRetryService.ExecuteAsync(() =>
            _db.TrueUps.AsNoTracking().ToListAsync());
        var allAdjustments = await DbRetryService.ExecuteAsync(() =>
            _db.ManualAdjustments.AsNoTracking().ToListAsync());

        ProgressService.Report(30, "Detecting underpayments...");
        var underpayments = DetectUnderpayments(allInvoices, allTrueUps, allAdjustments, children);

        ProgressService.Report(50, "Detecting payment gaps...");
        var paymentGaps = DetectPaymentGaps(allInvoices, children);

        ProgressService.Report(65, "Detecting coverage gaps...");
        var coverageGaps = DetectCoverageGaps(children);

        ProgressService.Report(80, "Detecting low payments...");
        var lowPayments = DetectLowPayments(allInvoices, children);

        ProgressService.Report(100, "Complete");
        ProgressService.Complete();

        return new ProblemSummary(underpayments, paymentGaps, coverageGaps, lowPayments);
    }

    private List<UnderpaymentProblem> DetectUnderpayments(
        List<Invoice> allInvoices, List<TrueUp> allTrueUps,
        List<ManualAdjustment> allAdjustments, List<Child> children)
    {
        var problems = new List<UnderpaymentProblem>();
        var childDict = children.ToDictionary(c => c.ChildId);
        var voucherDict = children.SelectMany(c => c.Vouchers)
            .ToDictionary(v => v.VoucherId);

        foreach (var inv in allInvoices.Where(i =>
            i.VODiscrepancy.HasValue && i.VODiscrepancy < 0))
        {
            if (!childDict.TryGetValue(inv.ChildId, out var child)) continue;
            if (!inv.VoucherId.HasValue ||
                !voucherDict.TryGetValue(inv.VoucherId.Value, out var voucher)) continue;

            var tuApplied = allTrueUps.Where(t => t.InvoiceId == inv.InvoiceId)
                .Sum(t => t.TrueUpAdjustAmount);
            var manApplied = allAdjustments.Where(a => a.InvoiceId == inv.InvoiceId &&
                (a.ApplyTo == "Balance" || a.ApplyTo == "Balance from Credit"))
                .Sum(a => a.Amount);

            decimal shortfall = Math.Abs(inv.VODiscrepancy!.Value);
            decimal remaining = -shortfall + tuApplied + manApplied;

            string issueType = inv.PaymentTotal == 0 ? "MISSED PAYMENT"
                : (tuApplied != 0 || manApplied != 0) ? "PARTIAL TRUE-UP"
                : "UNDERPAYMENT";

            problems.Add(new UnderpaymentProblem(inv, child, voucher,
                shortfall, tuApplied, manApplied, remaining, issueType, remaining >= 0));
        }
        return problems;
    }

    private List<PaymentGapProblem> DetectPaymentGaps(
        List<Invoice> allInvoices, List<Child> children)
    {
        var problems = new List<PaymentGapProblem>();
        var childDict = children.ToDictionary(c => c.ChildId);
        var voucherDict = children.SelectMany(c => c.Vouchers)
            .ToDictionary(v => v.VoucherId);

        var byVoucher = allInvoices.Where(i => i.VoucherId.HasValue)
            .GroupBy(i => i.VoucherId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.InvoiceStart).ToList());

        foreach (var (voucherId, invoices) in byVoucher)
        {
            if (!voucherDict.TryGetValue(voucherId, out var voucher)) continue;
            if (!childDict.TryGetValue(voucher.ChildId, out var child)) continue;

            for (int i = 0; i < invoices.Count - 1; i++)
            {
                var current = invoices[i];
                var next = invoices[i + 1];
                int gap = (next.InvoiceStart - current.InvoiceEnd).Days;
                if (gap <= GapThresholdDays) continue;

                var voucherEnd = voucher.TerminationDate ?? voucher.PeriodEnd;
                if (voucherEnd.HasValue && current.InvoiceEnd > voucherEnd) continue;
                if (current.InvoiceEnd < voucher.PeriodStart) continue;

                problems.Add(new PaymentGapProblem(child, voucher, current, next, gap,
                    $"{gap} day gap between invoice {current.InvoiceNumber} " +
                    $"({current.InvoiceEnd:MM/dd/yyyy}) and " +
                    $"{next.InvoiceNumber} ({next.InvoiceStart:MM/dd/yyyy})"));
            }
        }
        return problems;
    }

    private List<CoverageGapProblem> DetectCoverageGaps(List<Child> children)
    {
        var problems = new List<CoverageGapProblem>();
        foreach (var child in children)
        {
            var vouchers = child.Vouchers
                .Where(v => v.PeriodStart <= DateTime.Today)
                .OrderBy(v => v.PeriodStart).ToList();
            if (!vouchers.Any()) continue;

            for (int i = 0; i < vouchers.Count - 1; i++)
            {
                var current = vouchers[i];
                var next = vouchers[i + 1];
                var currentEnd = current.TerminationDate ?? current.PeriodEnd;
                if (!currentEnd.HasValue) continue;
                int gap = (next.PeriodStart - currentEnd.Value).Days;
                if (gap <= 0) continue;

                decimal dailyRate = current.GetDailyHHDCRateForDate(currentEnd.Value);
                int bussDays = NetworkDaysHelper.Calculate(
                    currentEnd.Value.AddDays(1), next.PeriodStart.AddDays(-1));
                decimal liability = bussDays * dailyRate;

                problems.Add(new CoverageGapProblem(child, current, next,
                    currentEnd.Value.AddDays(1), next.PeriodStart.AddDays(-1),
                    gap, liability,
                    $"{gap} day gap between voucher {current.VoucherNumber} " +
                    $"(ends {currentEnd.Value:MM/dd/yyyy}) and " +
                    $"{next.VoucherNumber} (starts {next.PeriodStart:MM/dd/yyyy})"));
            }

            var lastVoucher = vouchers.Last();
            var lastEnd = lastVoucher.TerminationDate ?? lastVoucher.PeriodEnd;
            if (lastEnd.HasValue && lastEnd.Value < DateTime.Today)
            {
                int gap = (DateTime.Today - lastEnd.Value).Days;
                if (gap > GapThresholdDays)
                {
                    decimal dailyRate = lastVoucher.GetDailyHHDCRateForDate(lastEnd.Value);
                    int bussDays = NetworkDaysHelper.Calculate(
                        lastEnd.Value.AddDays(1), DateTime.Today);
                    decimal liability = bussDays * dailyRate;

                    problems.Add(new CoverageGapProblem(child, lastVoucher, null,
                        lastEnd.Value.AddDays(1), DateTime.Today, gap, liability,
                        $"Voucher {lastVoucher.VoucherNumber} expired " +
                        $"{lastEnd.Value:MM/dd/yyyy} — no new voucher. " +
                        $"{gap} days uncovered."));
                }
            }
        }
        return problems;
    }

    private List<LowPaymentProblem> DetectLowPayments(
        List<Invoice> allInvoices, List<Child> children)
    {
        var problems = new List<LowPaymentProblem>();
        var childDict = children.ToDictionary(c => c.ChildId);
        var voucherDict = children.SelectMany(c => c.Vouchers)
            .ToDictionary(v => v.VoucherId);

        var byVoucher = allInvoices
            .Where(i => i.VoucherId.HasValue && i.PaymentTotal > 0)
            .GroupBy(i => i.VoucherId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (voucherId, invoices) in byVoucher)
        {
            if (!voucherDict.TryGetValue(voucherId, out var voucher)) continue;
            if (!childDict.TryGetValue(voucher.ChildId, out var child)) continue;

            decimal baseline;
            if (invoices.Count >= MinInvoicesForMedian)
            {
                var sorted = invoices.Select(i => i.PaymentTotal).OrderBy(p => p).ToList();
                int mid = sorted.Count / 2;
                baseline = sorted.Count % 2 == 0
                    ? (sorted[mid - 1] + sorted[mid]) / 2
                    : sorted[mid];
            }
            else
            {
                var latest = invoices.OrderByDescending(i => i.InvoiceStart).First();
                if (!latest.DaysBilled.HasValue || !latest.DailyVORate.HasValue) continue;
                baseline = latest.DaysBilled.Value * latest.DailyVORate.Value;
            }

            if (baseline <= 0) continue;

            foreach (var inv in invoices)
            {
                double pct = (double)inv.PaymentTotal / (double)baseline;
                if (pct < LowPaymentThreshold)
                {
                    problems.Add(new LowPaymentProblem(inv, child, voucher,
                        baseline, inv.PaymentTotal, (decimal)(pct * 100),
                        $"Payment ${inv.PaymentTotal:N2} is {pct:P0} of median " +
                        $"${baseline:N2} for voucher {voucher.VoucherNumber}"));
                }
            }
        }
        return problems;
    }

    public async Task<List<string>> GetChildProblemsAsync(int childId)
    {
        var child = await DbRetryService.ExecuteAsync(() =>
            _db.Children.AsNoTracking().Include(c => c.Vouchers)
                .FirstOrDefaultAsync(c => c.ChildId == childId));
        if (child == null) return new List<string>();

        var invoices = await DbRetryService.ExecuteAsync(() =>
            _db.Invoices.AsNoTracking()
                .Where(i => i.ChildId == childId && !i.IsUnresolved)
                .OrderBy(i => i.InvoiceStart).ToListAsync());

        var flags = new List<string>();

        foreach (var inv in invoices.Where(i =>
            i.VODiscrepancy.HasValue && i.VODiscrepancy < 0))
            flags.Add($"Underpayment on invoice {inv.InvoiceNumber} " +
                      $"— ${Math.Abs(inv.VODiscrepancy!.Value):N2} shortfall");

        var byVoucher = invoices.Where(i => i.VoucherId.HasValue)
            .GroupBy(i => i.VoucherId!.Value);
        foreach (var group in byVoucher)
        {
            var ordered = group.OrderBy(i => i.InvoiceStart).ToList();
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                int gap = (ordered[i + 1].InvoiceStart - ordered[i].InvoiceEnd).Days;
                if (gap > GapThresholdDays)
                    flags.Add($"{gap} day payment gap after invoice {ordered[i].InvoiceNumber}");
            }
        }

        var vouchers = child.Vouchers.OrderBy(v => v.PeriodStart).ToList();
        for (int i = 0; i < vouchers.Count - 1; i++)
        {
            var end = vouchers[i].TerminationDate ?? vouchers[i].PeriodEnd;
            if (!end.HasValue) continue;
            int gap = (vouchers[i + 1].PeriodStart - end.Value).Days;
            if (gap > 0)
                flags.Add($"{gap} day coverage gap between vouchers " +
                          $"{vouchers[i].VoucherNumber} and {vouchers[i + 1].VoucherNumber}");
        }

        var last = vouchers.LastOrDefault();
        if (last != null)
        {
            var lastEnd = last.TerminationDate ?? last.PeriodEnd;
            if (lastEnd.HasValue && lastEnd.Value < DateTime.Today &&
                (DateTime.Today - lastEnd.Value).Days > GapThresholdDays)
                flags.Add($"Voucher {last.VoucherNumber} expired " +
                          $"{lastEnd.Value:MM/dd/yyyy} — no active voucher");
        }

        return flags;
    }
}
