using HHDCTracker.Data;
using HHDCTracker.Helpers;
using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Services;

public class ImportService
{
    private readonly AppDbContext _db;
    private readonly NameMatchService _nameMatcher;
    private readonly int _locationId;

    public ImportService(AppDbContext db, int locationId)
    {
        _db = db;
        _locationId = locationId;
        _nameMatcher = new NameMatchService(db);
    }

    public record PaymentRow(
        string ChildNameMSDE, string ScholarshipId, int Absences,
        decimal MDExcels, decimal Scholarship, decimal PaymentTotal,
        string PCACodeLabel, bool HasTrueUpFlag);

    // Note: ClosureDays removed — MSDE uses straight Mon-Fri, no closures
    public record PaymentImportSession(
        string InvoiceNumber, DateTime InvoiceStart, DateTime InvoiceEnd,
        DateTime? PaymentDate);

    public record TrueUpRow(
        string Reason, string ScholarshipNumber, string ChildNameMSDE,
        string FirstAPInvoice, string SecondAPInvoice, string APReconcilingMonth,
        decimal APAmount, int AdjustDays, decimal TrueUpAdjustAmount);

    public record TrueUpImportSession(string? InvoiceNumber, string TrueUpType);

    public record ImportResult(
        int RowsImported, int RowsSkipped, int RowsUnresolved,
        List<string> SkippedDetails, List<string> UnresolvedDetails, List<string> Errors);

    public record DuplicateCheckResult(List<string> Duplicates);

    // ── MSDE Expected Calculation ─────────────────────────────────────────
    /// <summary>
    /// Calculates MSDE VOExpectedTotal using confirmed formula:
    ///   DailyRate = VOPromisedWeekly / 5
    ///   MonthlyExpected = DailyRate × MonthBusinessDays (straight Mon-Fri)
    ///   Full period  → MonthlyExpected / 2
    ///   Partial month → MonthlyExpected × (VoucherActiveBusinessDays / MonthBusinessDays)
    /// </summary>
    public static decimal CalculateVOExpected(
        decimal weeklyRate,
        DateTime invoiceStart,
        DateTime invoiceEnd,
        DateTime voucherPeriodStart,
        DateTime? voucherPeriodEnd)
    {
        decimal dailyRate = weeklyRate / 5m;
        int year = invoiceStart.Year;
        int month = invoiceStart.Month;

        int monthBizDays = NetworkDaysHelper.MonthBusinessDays(year, month);
        if (monthBizDays == 0) return 0;

        decimal monthlyExpected = dailyRate * monthBizDays;

        bool voucherCoversFullInvoice =
            voucherPeriodStart <= invoiceStart &&
            (!voucherPeriodEnd.HasValue || voucherPeriodEnd.Value >= invoiceEnd);

        if (voucherCoversFullInvoice)
            return Math.Round(monthlyExpected / 2m, 2);

        int voucherBizDays = NetworkDaysHelper.VoucherBusinessDaysInMonth(
            voucherPeriodStart, voucherPeriodEnd, year, month);

        return Math.Round(monthlyExpected * ((decimal)voucherBizDays / monthBizDays), 2);
    }

    // ── Payment Validation ────────────────────────────────────────────────
    public async Task<List<string>> ValidatePaymentNamesAsync(List<PaymentRow> rows)
    {
        var errors = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.HasTrueUpFlag) continue;
            var match = await _nameMatcher.FindChildAsync(row.ChildNameMSDE, _locationId);
            if (match.Child == null) continue;
            var voucher = await DbRetryService.ExecuteAsync(() =>
                _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v =>
                    v.VoucherNumber == row.ScholarshipId
                    && v.ChildId == match.Child.ChildId));
            if (voucher == null && match.MatchType == "Exact")
                errors.Add($"Row {i + 1}: Voucher {row.ScholarshipId} does not " +
                           $"belong to {match.Child.FullName}.");
        }
        return errors;
    }

    public async Task<DuplicateCheckResult> CheckPaymentDuplicatesAsync(
        List<PaymentRow> rows, PaymentImportSession session)
    {
        var dupes = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.HasTrueUpFlag) continue;
            var exists = await DbRetryService.ExecuteAsync(() =>
                _db.Invoices.AsNoTracking().AnyAsync(inv =>
                    inv.InvoiceNumber == session.InvoiceNumber &&
                    inv.RawVoucherNumber == row.ScholarshipId));
            if (exists)
                dupes.Add($"Row {i + 1}: Invoice {session.InvoiceNumber} / " +
                          $"Voucher {row.ScholarshipId} ({row.ChildNameMSDE})");
        }
        return new DuplicateCheckResult(dupes);
    }

    // ── Payment Import ────────────────────────────────────────────────────
    public async Task<ImportResult> ImportPaymentsAsync(
        List<PaymentRow> rows, PaymentImportSession session,
        int importedByUserId, Action<int, int>? progressCallback = null)
    {
        int imported = 0, skipped = 0, unresolved = 0;
        var skippedDetails = new List<string>();
        var unresolvedDetails = new List<string>();
        var errors = new List<string>();

        var importSession = new ImportSession
        {
            ImportType = "Payment", ImportedByUserId = importedByUserId,
            LocationId = _locationId, InvoiceNumber = session.InvoiceNumber,
            InvoiceStart = session.InvoiceStart, InvoiceEnd = session.InvoiceEnd,
            PaymentDate = session.PaymentDate, ClosureDays = 0
        };
        _db.ImportSessions.Add(importSession);
        await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());

        int total = rows.Count;
        for (int i = 0; i < total; i++)
        {
            progressCallback?.Invoke(i + 1, total);
            var row = rows[i];

            if (row.HasTrueUpFlag)
            {
                skipped++;
                skippedDetails.Add($"Row {i + 1}: {row.ScholarshipId} — True-Up flag");
                continue;
            }

            try
            {
                var match = await _nameMatcher.FindChildAsync(row.ChildNameMSDE, _locationId);
                var child = match.Child;

                Voucher? voucher = null;
                if (child != null)
                    voucher = await DbRetryService.ExecuteAsync(() =>
                        _db.Vouchers.AsNoTracking().Where(v =>
                            v.VoucherNumber == row.ScholarshipId
                            && v.ChildId == child.ChildId
                            && v.PeriodStart <= session.InvoiceStart
                            && (v.PeriodEnd == null || v.PeriodEnd >= session.InvoiceStart)
                            && (v.TerminationDate == null
                                || v.TerminationDate >= session.InvoiceStart))
                        .FirstOrDefaultAsync());

                bool isUnresolved = child == null || voucher == null;
                int daysBilled = NetworkDaysHelper.Calculate(
                    session.InvoiceStart, session.InvoiceEnd);

                decimal dailyVORate = 0, dailyHHDCRate = 0;
                decimal voExpected = 0, hhdcExpected = 0;

                if (voucher != null)
                {
                    dailyVORate = voucher.GetDailyVORateForDate(session.InvoiceStart);
                    dailyHHDCRate = voucher.GetDailyHHDCRateForDate(session.InvoiceStart);
                    var voucherEnd = voucher.TerminationDate ?? voucher.PeriodEnd;
                    voExpected = CalculateVOExpected(voucher.VOPromisedWeekly,
                        session.InvoiceStart, session.InvoiceEnd,
                        voucher.PeriodStart, voucherEnd);
                    hhdcExpected = CalculateVOExpected(voucher.HHDCChargeWeekly,
                        session.InvoiceStart, session.InvoiceEnd,
                        voucher.PeriodStart, voucherEnd);
                }

                if (isUnresolved)
                {
                    unresolved++;
                    unresolvedDetails.Add($"{row.ScholarshipId} ({row.ChildNameMSDE})");
                    if (child != null) child.HasUnresolved = true;

                    int childId = child?.ChildId ?? 0;
                    await DbRetryService.ExecuteAsync(() =>
                        _db.Database.ExecuteSqlRawAsync(
                            @"INSERT INTO Invoices
                            (VoucherId, ChildId, InvoiceNumber, ImportSessionId,
                             InvoiceStart, InvoiceEnd, PaymentDate, AbsenceDays,
                             ClosureDays, DailyVORate, DailyHHDCRate, DaysBilled,
                             VOExpectedTotal, HHDCExpectedTotal, MDExcelsAmount,
                             ScholarshipAmount, PaymentTotal, VODiscrepancy,
                             HHDCSurplus, HasTrueUpFlag, IsUnresolved,
                             RawVoucherNumber, RawChildName, ImportedByUserId, ImportedAt)
                            VALUES (NULL,{0},{1},{2},{3},{4},{5},{6},0,
                             '0','0',{7},'0','0',{8},{9},{10},'0','0',0,1,
                             {11},{12},{13},{14})",
                            childId, session.InvoiceNumber,
                            importSession.ImportSessionId,
                            session.InvoiceStart.ToString("yyyy-MM-dd"),
                            session.InvoiceEnd.ToString("yyyy-MM-dd"),
                            session.PaymentDate?.ToString("yyyy-MM-dd"),
                            row.Absences, daysBilled,
                            row.MDExcels, row.Scholarship, row.PaymentTotal,
                            row.ScholarshipId, row.ChildNameMSDE,
                            importedByUserId,
                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));

                    if (child != null)
                        await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
                    imported++;
                    continue;
                }

                _db.Invoices.Add(new Invoice
                {
                    VoucherId = voucher!.VoucherId, ChildId = child!.ChildId,
                    InvoiceNumber = session.InvoiceNumber,
                    ImportSessionId = importSession.ImportSessionId,
                    InvoiceStart = session.InvoiceStart, InvoiceEnd = session.InvoiceEnd,
                    PaymentDate = session.PaymentDate,
                    AbsenceDays = row.Absences, ClosureDays = 0,
                    DailyVORate = dailyVORate, DailyHHDCRate = dailyHHDCRate,
                    DaysBilled = daysBilled,
                    VOExpectedTotal = voExpected, HHDCExpectedTotal = hhdcExpected,
                    MDExcelsAmount = row.MDExcels, ScholarshipAmount = row.Scholarship,
                    PaymentTotal = row.PaymentTotal,
                    VODiscrepancy = row.PaymentTotal - voExpected,
                    HHDCSurplus = hhdcExpected - row.PaymentTotal,
                    HasTrueUpFlag = false, IsUnresolved = false,
                    RawVoucherNumber = row.ScholarshipId, RawChildName = row.ChildNameMSDE,
                    ImportedByUserId = importedByUserId
                });
                await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
                imported++;
            }
            catch (Exception ex) { errors.Add($"Row {i + 1}: {ex.Message}"); }
        }

        importSession.RowsImported = imported;
        importSession.RowsSkipped = skipped;
        importSession.RowsUnresolved = unresolved;
        await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
        return new ImportResult(imported, skipped, unresolved,
            skippedDetails, unresolvedDetails, errors);
    }

    // ── True-Up Validation ────────────────────────────────────────────────
    public async Task<List<string>> ValidateTrueUpNamesAsync(List<TrueUpRow> rows)
    {
        var errors = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Reason.ToLower().Contains("new voucher")) continue;
            var match = await _nameMatcher.FindChildAsync(row.ChildNameMSDE, _locationId);
            if (match.Child == null) continue;
            var voucher = await DbRetryService.ExecuteAsync(() =>
                _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v =>
                    v.VoucherNumber == row.ScholarshipNumber
                    && v.ChildId == match.Child.ChildId));
            if (voucher == null && match.MatchType == "Exact")
                errors.Add($"Row {i + 1}: Voucher {row.ScholarshipNumber} does not " +
                           $"belong to {match.Child.FullName}.");
        }
        return errors;
    }

    public async Task<DuplicateCheckResult> CheckTrueUpDuplicatesAsync(
        List<TrueUpRow> rows, TrueUpImportSession session)
    {
        var dupes = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var exists = await DbRetryService.ExecuteAsync(() =>
                _db.TrueUps.AsNoTracking().AnyAsync(t =>
                    t.FirstAPInvoiceNumber == row.FirstAPInvoice &&
                    t.APReconcilingMonth == row.APReconcilingMonth &&
                    t.TrueUpType == session.TrueUpType &&
                    t.Voucher!.VoucherNumber == row.ScholarshipNumber));
            if (exists)
                dupes.Add($"Row {i + 1}: {row.ScholarshipNumber} / " +
                          $"{row.APReconcilingMonth} ({row.ChildNameMSDE})");
        }
        return new DuplicateCheckResult(dupes);
    }

    // ── True-Up Import ────────────────────────────────────────────────────
    public async Task<ImportResult> ImportTrueUpsAsync(
        List<TrueUpRow> rows, TrueUpImportSession session,
        int importedByUserId, Action<int, int>? progressCallback = null)
    {
        int imported = 0, unresolved = 0;
        var unresolvedDetails = new List<string>();
        var errors = new List<string>();

        var importSession = new ImportSession
        {
            ImportType = "TrueUp", ImportedByUserId = importedByUserId,
            LocationId = _locationId, InvoiceNumber = session.InvoiceNumber
        };
        _db.ImportSessions.Add(importSession);
        await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());

        int total = rows.Count;
        for (int i = 0; i < total; i++)
        {
            progressCallback?.Invoke(i + 1, total);
            var row = rows[i];
            try
            {
                var match = await _nameMatcher.FindChildAsync(row.ChildNameMSDE, _locationId);
                var child = match.Child;
                var voucher = child != null
                    ? await DbRetryService.ExecuteAsync(() =>
                        _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v =>
                            v.VoucherNumber == row.ScholarshipNumber
                            && v.ChildId == child.ChildId))
                    : null;

                bool isNewVoucher = row.Reason.ToLower().Contains("new voucher");

                if (child == null || voucher == null)
                {
                    unresolved++;
                    unresolvedDetails.Add($"{row.ScholarshipNumber} ({row.ChildNameMSDE})");
                    if (child != null) child.HasUnresolved = true;
                    _db.UnresolvedTrueUps.Add(new UnresolvedTrueUp
                    {
                        ImportSessionId = importSession.ImportSessionId,
                        RawVoucherNumber = row.ScholarshipNumber,
                        RawChildName = row.ChildNameMSDE,
                        TrueUpType = session.TrueUpType, Reason = row.Reason,
                        FirstAPInvoiceNumber = row.FirstAPInvoice == "None"
                            ? null : row.FirstAPInvoice,
                        SecondAPInvoiceNumber = row.SecondAPInvoice == "None"
                            ? null : row.SecondAPInvoice,
                        APReconcilingMonth = row.APReconcilingMonth,
                        AdjustDays = row.AdjustDays,
                        TrueUpAdjustAmount = row.TrueUpAdjustAmount,
                        APAmount = row.APAmount, LocationId = _locationId,
                        ImportedByUserId = importedByUserId
                    });
                    await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
                    continue;
                }

                var invoiceRef = isNewVoucher
                    ? null : (session.InvoiceNumber ?? row.FirstAPInvoice);
                Invoice? invoice = null;
                if (!string.IsNullOrEmpty(invoiceRef))
                    invoice = await DbRetryService.ExecuteAsync(() =>
                        _db.Invoices.AsNoTracking().FirstOrDefaultAsync(inv =>
                            inv.InvoiceNumber == invoiceRef
                            && inv.VoucherId == voucher.VoucherId));

                _db.TrueUps.Add(new TrueUp
                {
                    VoucherId = voucher.VoucherId, ChildId = child.ChildId,
                    ImportSessionId = importSession.ImportSessionId,
                    InvoiceId = invoice?.InvoiceId,
                    InvoiceNumber = isNewVoucher ? null : invoiceRef,
                    TrueUpType = session.TrueUpType, Reason = row.Reason,
                    FirstAPInvoiceNumber = row.FirstAPInvoice == "None"
                        ? null : row.FirstAPInvoice,
                    SecondAPInvoiceNumber = row.SecondAPInvoice == "None"
                        ? null : row.SecondAPInvoice,
                    APReconcilingMonth = row.APReconcilingMonth,
                    AdjustDays = row.AdjustDays,
                    TrueUpAdjustAmount = row.TrueUpAdjustAmount,
                    APAmount = row.APAmount, ImportedByUserId = importedByUserId
                });
                await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
                imported++;
            }
            catch (Exception ex) { errors.Add($"Row {i + 1}: {ex.Message}"); }
        }

        importSession.RowsImported = imported;
        importSession.RowsUnresolved = unresolved;
        await DbRetryService.ExecuteAsync(() => _db.SaveChangesAsync());
        return new ImportResult(imported, 0, unresolved,
            [], unresolvedDetails, errors);
    }
}
