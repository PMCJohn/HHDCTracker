using HHDCTracker.Models;
using HHDCTracker.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HHDCTracker.Views.Dashboard;

public partial class UnresolvedView : UserControl
{
    private DispatcherTimer? _notifTimer;

    public UnresolvedView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadInvoicesAsync();
        await LoadTrueUpsAsync();
    }

    // ── INVOICES ──────────────────────────────────────────────────────────
    private async Task LoadInvoicesAsync()
    {
        var locationChildIds = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Children.AsNoTracking()
                .Where(c => c.LocationId == App.CurrentLocation!.LocationId)
                .Select(c => c.ChildId).ToListAsync());

        var invoices = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Invoices.AsNoTracking()
                .Where(i => i.IsUnresolved)
                .OrderByDescending(i => i.ImportedAt)
                .ToListAsync());

        var filtered = invoices
            .Where(i => i.ChildId == 0 || locationChildIds.Contains(i.ChildId))
            .ToList();

        var flagged = AnalyzeUnresolvedInvoices(filtered);
        InvoiceGrid.ItemsSource = flagged;
        InvoiceCountText.Text = filtered.Count > 0
            ? $"{filtered.Count} unresolved invoice(s)"
            : "No unresolved invoices";
        InvoiceLastRefresh.Text = $"Last refreshed @ {DateTime.Now:MM/dd/yyyy HH:mm}";
    }

    private List<UnresolvedInvoiceRow> AnalyzeUnresolvedInvoices(List<Invoice> invoices)
    {
        var byVoucher = invoices
            .GroupBy(i => i.RawVoucherNumber ?? "")
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.InvoiceStart).ToList());

        var result = new List<UnresolvedInvoiceRow>();
        foreach (var inv in invoices)
        {
            var flags = new List<string>();
            var vNum = inv.RawVoucherNumber ?? "";

            if (byVoucher.TryGetValue(vNum, out var group) && group.Count > 1)
            {
                var idx = group.IndexOf(inv);
                if (idx > 0)
                {
                    int gap = (inv.InvoiceStart - group[idx - 1].InvoiceEnd).Days;
                    if (gap > 20)
                        flags.Add($"Gap: {gap} days since previous invoice");
                }

                if (group.Count >= 3)
                {
                    var sorted = group.Select(i => i.PaymentTotal).OrderBy(p => p).ToList();
                    int mid = sorted.Count / 2;
                    decimal median = sorted.Count % 2 == 0
                        ? (sorted[mid - 1] + sorted[mid]) / 2
                        : sorted[mid];
                    if (median > 0 && inv.PaymentTotal < median * 0.95m)
                        flags.Add($"Low: ${inv.PaymentTotal:N2} vs median ${median:N2}");
                }
            }
            result.Add(new UnresolvedInvoiceRow(inv, flags));
        }
        return result;
    }

    // ── TRUE-UPS ──────────────────────────────────────────────────────────
    private async Task LoadTrueUpsAsync()
    {
        var trueUps = await DbRetryService.ExecuteAsync(() =>
            App.Db!.UnresolvedTrueUps.AsNoTracking()
                .Where(t => !t.IsResolved
                    && t.LocationId == App.CurrentLocation!.LocationId)
                .OrderByDescending(t => t.ImportedAt)
                .ToListAsync());

        TrueUpGrid.ItemsSource = trueUps;
        TrueUpCountText.Text = trueUps.Count > 0
            ? $"{trueUps.Count} unresolved true-up(s)"
            : "No unresolved true-ups";
        TrueUpLastRefresh.Text = $"Last refreshed @ {DateTime.Now:MM/dd/yyyy HH:mm}";
    }

    private void InvoiceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void RefreshInvoices_Click(object sender, RoutedEventArgs e)
        => await LoadInvoicesAsync();

    private async void RefreshTrueUps_Click(object sender, RoutedEventArgs e)
        => await LoadTrueUpsAsync();

    // ── RESOLVE INVOICE ───────────────────────────────────────────────────
    private async void ResolveInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int invoiceId) return;

        // Concurrency-safe: reload from DB before acting
        var invoice = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == invoiceId));
        if (invoice == null || !invoice.IsUnresolved)
        {
            ShowNotification("This entry was already resolved by another user.", isError: true);
            await LoadInvoicesAsync();
            return;
        }

        var dlg = new ResolveEntryDialog(
            invoice.RawChildName ?? "", invoice.RawVoucherNumber ?? "",
            "Invoice", invoice.InvoiceNumber ?? "",
            $"Invoice {invoice.InvoiceNumber} | " +
            $"{invoice.InvoiceStart:MM/dd/yyyy} – {invoice.InvoiceEnd:MM/dd/yyyy} | " +
            $"${invoice.PaymentTotal:N2}");
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true || dlg.SelectedChild == null) return;

        var child = dlg.SelectedChild;

        // Check if alias prompt needed
        var nameMatches = child.FullName.Equals(
            invoice.RawChildName, StringComparison.OrdinalIgnoreCase);
        if (!nameMatches && !string.IsNullOrEmpty(invoice.RawChildName))
        {
            var aliasMatch = await App.Db!.ChildAliases.AsNoTracking()
                .AnyAsync(a => a.ChildId == child.ChildId && a.AliasName == invoice.RawChildName);
            if (!aliasMatch)
            {
                var res = MessageBox.Show(
                    $"The MSDE name \"{invoice.RawChildName}\" doesn't exactly match " +
                    $"\"{child.FullName}\".\n\nSave as an alias?",
                    "Save Alias?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    App.Db!.ChildAliases.Add(new ChildAlias
                    {
                        ChildId = child.ChildId, AliasName = invoice.RawChildName,
                        CreatedByUserId = App.CurrentUser!.UserId
                    });
            }
        }

        var voucher = dlg.SelectedVoucher;
        if (voucher == null)
        {
            MessageBox.Show("Please select a voucher to link this invoice to.",
                "No Voucher Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Recalculate stored fields using confirmed MSDE formula
        var dailyVO = voucher.GetDailyVORateForDate(invoice.InvoiceStart);
        var dailyHHDC = voucher.GetDailyHHDCRateForDate(invoice.InvoiceStart);
        int daysBilled = Helpers.NetworkDaysHelper.Calculate(
            invoice.InvoiceStart, invoice.InvoiceEnd);

        decimal voExpected = ImportService.CalculateVOExpected(
            voucher.VOPromisedWeekly,
            invoice.InvoiceStart, invoice.InvoiceEnd,
            voucher.PeriodStart, voucher.TerminationDate ?? voucher.PeriodEnd);

        decimal hhdcExpected = ImportService.CalculateVOExpected(
            voucher.HHDCChargeWeekly,
            invoice.InvoiceStart, invoice.InvoiceEnd,
            voucher.PeriodStart, voucher.TerminationDate ?? voucher.PeriodEnd);

        invoice.ChildId = child.ChildId;
        invoice.VoucherId = voucher.VoucherId;
        invoice.DailyVORate = dailyVO;
        invoice.DailyHHDCRate = dailyHHDC;
        invoice.DaysBilled = daysBilled;
        invoice.VOExpectedTotal = voExpected;
        invoice.HHDCExpectedTotal = hhdcExpected;
        invoice.VODiscrepancy = invoice.PaymentTotal - voExpected;
        invoice.HHDCSurplus = hhdcExpected - invoice.PaymentTotal;

        // Clear HasUnresolved if no more unresolved entries for this child
        var hasMore = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Invoices.AnyAsync(i =>
                i.ChildId == child.ChildId && i.IsUnresolved && i.InvoiceId != invoiceId));
        if (!hasMore)
        {
            var dbChild = await App.Db!.Children.FindAsync(child.ChildId);
            if (dbChild != null) dbChild.HasUnresolved = false;
        }

        await DbRetryService.ExecuteAsync(() => App.Db!.SaveChangesAsync());
        ShowNotification($"Invoice {invoice.InvoiceNumber} resolved and linked to {child.FullName}.");
        await LoadInvoicesAsync();
    }

    // ── DELETE INVOICE ────────────────────────────────────────────────────
    private async void DeleteInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int invoiceId) return;

        var result = MessageBox.Show(
            "Delete this unresolved invoice entry? This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // Concurrency-safe reload
        var invoice = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == invoiceId));
        if (invoice == null)
        {
            ShowNotification("Entry already deleted.", isError: true);
            await LoadInvoicesAsync();
            return;
        }

        App.Db!.Invoices.Remove(invoice);

        if (invoice.ChildId > 0)
        {
            var hasMore = await DbRetryService.ExecuteAsync(() =>
                App.Db.Invoices.AnyAsync(i =>
                    i.ChildId == invoice.ChildId && i.IsUnresolved && i.InvoiceId != invoiceId));
            if (!hasMore)
            {
                var child = await App.Db.Children.FindAsync(invoice.ChildId);
                if (child != null) child.HasUnresolved = false;
            }
        }

        await DbRetryService.ExecuteAsync(() => App.Db!.SaveChangesAsync());
        ShowNotification("Entry deleted.");
        await LoadInvoicesAsync();
    }

    // ── RESOLVE TRUE-UP ───────────────────────────────────────────────────
    private async void ResolveTrueUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int tuId) return;

        var unresolvedTU = await DbRetryService.ExecuteAsync(() =>
            App.Db!.UnresolvedTrueUps.FirstOrDefaultAsync(t => t.UnresolvedTrueUpId == tuId));
        if (unresolvedTU == null || unresolvedTU.IsResolved)
        {
            ShowNotification("This entry was already resolved by another user.", isError: true);
            await LoadTrueUpsAsync();
            return;
        }

        var dlg = new ResolveEntryDialog(
            unresolvedTU.RawChildName, unresolvedTU.RawVoucherNumber,
            "True-Up", unresolvedTU.FirstAPInvoiceNumber ?? "",
            $"{unresolvedTU.TrueUpType} | {unresolvedTU.APReconcilingMonth} | " +
            $"${unresolvedTU.TrueUpAdjustAmount:N2}");
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true || dlg.SelectedChild == null) return;

        var child = dlg.SelectedChild;
        var nameMatches = child.FullName.Equals(
            unresolvedTU.RawChildName, StringComparison.OrdinalIgnoreCase);

        if (!nameMatches && !string.IsNullOrEmpty(unresolvedTU.RawChildName))
        {
            var aliasMatch = await App.Db!.ChildAliases.AsNoTracking()
                .AnyAsync(a => a.ChildId == child.ChildId && a.AliasName == unresolvedTU.RawChildName);
            if (!aliasMatch)
            {
                var res = MessageBox.Show(
                    $"The MSDE name \"{unresolvedTU.RawChildName}\" doesn't exactly match " +
                    $"\"{child.FullName}\".\n\nSave as an alias?",
                    "Save Alias?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    App.Db!.ChildAliases.Add(new ChildAlias
                    {
                        ChildId = child.ChildId, AliasName = unresolvedTU.RawChildName,
                        CreatedByUserId = App.CurrentUser!.UserId
                    });
            }
        }

        var voucher = dlg.SelectedVoucher ?? await DbRetryService.ExecuteAsync(() =>
            App.Db!.Vouchers.FirstOrDefaultAsync(v =>
                v.VoucherNumber == unresolvedTU.RawVoucherNumber && v.ChildId == child.ChildId));

        if (voucher == null)
        {
            MessageBox.Show(
                $"No matching voucher found. Add voucher {unresolvedTU.RawVoucherNumber} " +
                $"to {child.FullName}'s profile first.",
                "No Voucher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Invoice? invoice = null;
        if (!string.IsNullOrEmpty(unresolvedTU.FirstAPInvoiceNumber))
            invoice = await DbRetryService.ExecuteAsync(() =>
                App.Db!.Invoices.FirstOrDefaultAsync(i =>
                    i.InvoiceNumber == unresolvedTU.FirstAPInvoiceNumber
                    && i.VoucherId == voucher.VoucherId));

        App.Db!.TrueUps.Add(new TrueUp
        {
            VoucherId = voucher.VoucherId, ChildId = child.ChildId,
            ImportSessionId = unresolvedTU.ImportSessionId,
            InvoiceId = invoice?.InvoiceId,
            InvoiceNumber = unresolvedTU.FirstAPInvoiceNumber,
            TrueUpType = unresolvedTU.TrueUpType, Reason = unresolvedTU.Reason,
            FirstAPInvoiceNumber = unresolvedTU.FirstAPInvoiceNumber,
            SecondAPInvoiceNumber = unresolvedTU.SecondAPInvoiceNumber,
            APReconcilingMonth = unresolvedTU.APReconcilingMonth,
            AdjustDays = unresolvedTU.AdjustDays,
            TrueUpAdjustAmount = unresolvedTU.TrueUpAdjustAmount,
            APAmount = unresolvedTU.APAmount,
            ImportedByUserId = unresolvedTU.ImportedByUserId
        });

        unresolvedTU.IsResolved = true;
        unresolvedTU.ResolvedChildId = child.ChildId;
        unresolvedTU.ResolvedVoucherId = voucher.VoucherId;
        unresolvedTU.ResolvedAt = DateTime.UtcNow;
        unresolvedTU.ResolvedByUserId = App.CurrentUser!.UserId;

        await DbRetryService.ExecuteAsync(() => App.Db!.SaveChangesAsync());
        ShowNotification($"True-up resolved and linked to {child.FullName}.");
        await LoadTrueUpsAsync();
    }

    // ── DELETE TRUE-UP ────────────────────────────────────────────────────
    private async void DeleteTrueUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int tuId) return;

        var result = MessageBox.Show(
            "Delete this unresolved true-up entry? This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var tu = await DbRetryService.ExecuteAsync(() =>
            App.Db!.UnresolvedTrueUps.FirstOrDefaultAsync(t => t.UnresolvedTrueUpId == tuId));
        if (tu == null)
        {
            ShowNotification("Entry already deleted.", isError: true);
            await LoadTrueUpsAsync();
            return;
        }

        App.Db!.UnresolvedTrueUps.Remove(tu);
        await DbRetryService.ExecuteAsync(() => App.Db!.SaveChangesAsync());
        ShowNotification("Entry deleted.");
        await LoadTrueUpsAsync();
    }

    // ── NOTIFICATION ──────────────────────────────────────────────────────
    private void ShowNotification(string message, bool isError = false)
    {
        NotificationBanner.Background = isError
            ? new SolidColorBrush(Color.FromRgb(250, 219, 216))
            : new SolidColorBrush(Color.FromRgb(213, 245, 227));
        NotificationText.Text = isError ? $"⚠  {message}" : $"✓  {message}";
        NotificationText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(146, 43, 33))
            : new SolidColorBrush(Color.FromRgb(30, 132, 73));
        NotificationBanner.Visibility = Visibility.Visible;
        _notifTimer?.Stop();
        _notifTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _notifTimer.Tick += (_, _) =>
        {
            NotificationBanner.Visibility = Visibility.Collapsed;
            _notifTimer.Stop();
        };
        _notifTimer.Start();
    }

    private void CloseNotification_Click(object sender, RoutedEventArgs e)
    {
        _notifTimer?.Stop();
        NotificationBanner.Visibility = Visibility.Collapsed;
    }

    // ── View Model ────────────────────────────────────────────────────────
    private record UnresolvedInvoiceRow(Invoice Invoice, List<string> Flags)
    {
        public int InvoiceId => Invoice.InvoiceId;
        public string? RawVoucherNumber => Invoice.RawVoucherNumber;
        public string? RawChildName => Invoice.RawChildName;
        public string? InvoiceNumber => Invoice.InvoiceNumber;
        public DateTime InvoiceStart => Invoice.InvoiceStart;
        public DateTime InvoiceEnd => Invoice.InvoiceEnd;
        public decimal PaymentTotal => Invoice.PaymentTotal;
        public DateTime ImportedAt => Invoice.ImportedAt;
        public string FlagText => Flags.Any() ? string.Join(" | ", Flags) : "";
        public bool HasFlags => Flags.Any();
    }
}
