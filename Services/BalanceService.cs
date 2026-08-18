using HHDCTracker.Data;
using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Services;

/// <summary>
/// Calculates live balance figures for a child.
/// All balances are computed fresh from the database — nothing is cached here.
/// </summary>
public class BalanceService
{
    private readonly AppDbContext _db;

    public BalanceService(AppDbContext db) => _db = db;

    public record ChildBalanceSummary(
        decimal LedgerBalance,      // sum of VODiscrepancy across all invoices
        decimal TrueUpBalance,      // sum of TrueUpAdjustAmount
        decimal ManualBalance,      // sum of Balance + Balance from Credit adjustments
        decimal RunningCredit,      // overpayments + Credit adjustments - BfC adjustments
        decimal NetBalance          // LedgerBalance + TrueUpBalance + ManualBalance
    );

    public async Task<ChildBalanceSummary> GetBalanceSummaryAsync(int childId)
    {
        var invoices = await _db.Invoices
            .Where(i => i.ChildId == childId && !i.IsUnresolved)
            .ToListAsync();

        var trueUps = await _db.TrueUps
            .Where(t => t.ChildId == childId)
            .ToListAsync();

        var adjustments = await _db.ManualAdjustments
            .Where(a => a.ChildId == childId)
            .ToListAsync();

        decimal ledgerBalance = invoices.Sum(i => i.VODiscrepancy ?? 0);
        decimal trueUpBalance = trueUps.Sum(t => t.TrueUpAdjustAmount);

        decimal manualBalance = adjustments
            .Where(a => a.ApplyTo == "Balance" || a.ApplyTo == "Balance from Credit")
            .Sum(a => a.Amount);

        // Running credit = overpayments from invoices (HHDCSurplus < 0 = voucher overpaid)
        //                + Credit manual adjustments
        //                - Balance from Credit adjustments (draws from credit)
        decimal overpaymentCredit = invoices
            .Where(i => i.HHDCSurplus.HasValue && i.HHDCSurplus < 0)
            .Sum(i => Math.Abs(i.HHDCSurplus!.Value));

        decimal creditAdjustments = adjustments
            .Where(a => a.ApplyTo == "Credit")
            .Sum(a => a.Amount);

        decimal creditDrawdowns = adjustments
            .Where(a => a.ApplyTo == "Balance from Credit")
            .Sum(a => a.Amount);

        decimal runningCredit = overpaymentCredit + creditAdjustments - creditDrawdowns;
        decimal netBalance = ledgerBalance + trueUpBalance + manualBalance;

        return new ChildBalanceSummary(
            ledgerBalance, trueUpBalance, manualBalance, runningCredit, netBalance);
    }

    /// <summary>
    /// Returns all invoices with open issues for a child —
    /// where VODiscrepancy is negative and not fully covered by true-ups or adjustments.
    /// </summary>
    public async Task<List<InvoiceProblem>> GetOpenProblemsAsync(int childId)
    {
        var invoices = await _db.Invoices
            .Include(i => i.TrueUps)
            .Include(i => i.ManualAdjustments)
            .Include(i => i.Voucher)
            .Where(i => i.ChildId == childId
                && !i.IsUnresolved
                && i.VODiscrepancy.HasValue
                && i.VODiscrepancy < 0)
            .ToListAsync();

        var problems = new List<InvoiceProblem>();
        foreach (var inv in invoices)
        {
            decimal trueUpApplied = inv.TrueUps.Sum(t => t.TrueUpAdjustAmount);
            decimal manualApplied = inv.ManualAdjustments
                .Where(a => a.ApplyTo == "Balance" || a.ApplyTo == "Balance from Credit")
                .Sum(a => a.Amount);

            decimal remaining = (inv.VODiscrepancy ?? 0) + trueUpApplied + manualApplied;

            string issueType = inv.PaymentTotal == 0 ? "MISSED PAYMENT"
                : trueUpApplied != 0 || manualApplied != 0 ? "PARTIAL TRUE-UP"
                : "UNDERPAYMENT";

            problems.Add(new InvoiceProblem(
                inv,
                IssueType: issueType,
                VOShortfall: Math.Abs(inv.VODiscrepancy ?? 0),
                TrueUpApplied: trueUpApplied,
                ManualApplied: manualApplied,
                Remaining: remaining,
                IsResolved: remaining >= 0
            ));
        }
        return problems;
    }

    public record InvoiceProblem(
        Invoice Invoice,
        string IssueType,
        decimal VOShortfall,
        decimal TrueUpApplied,
        decimal ManualApplied,
        decimal Remaining,
        bool IsResolved
    );
}
