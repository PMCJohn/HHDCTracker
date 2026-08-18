using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HHDCTracker.Views.Import;

public partial class PaymentImportView : UserControl
{
    private DispatcherTimer? _notifTimer;

    public PaymentImportView()
    {
        InitializeComponent();
    }

    // ── PARSE PASTE AREA ──────────────────────────────────────────────────
    private List<ImportService.PaymentRow> ParsePasteData()
    {
        var rows = new List<ImportService.PaymentRow>();
        var lines = PasteBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        // Skip header row if present
        if (lines.Count > 0 && lines[0].ToLower().Contains("child name"))
            lines = lines.Skip(1).ToList();

        foreach (var line in lines)
        {
            // Browser table copies as tab-separated
            var cols = line.Split('\t');
            if (cols.Length < 13) continue;

            // Col map: 0=ChildName, 1=Infant, 2=DOB, 3=RegFee(ignored),
            //          4=Absences, 5=CareUnits, 6=SpecialNeed,
            //          7=MDExcels, 8=Scholarship, 9=PayTotal,
            //          10=PCACode, 11=TrueUpFlag, 12=ScholarshipID
            rows.Add(new ImportService.PaymentRow(
                ChildNameMSDE: cols[0].Trim(),
                ScholarshipId: cols[12].Trim(),
                Absences: int.TryParse(cols[4].Trim(), out var abs) ? abs : 0,
                MDExcels: decimal.TryParse(cols[7].Trim(), out var mde) ? mde : 0,
                Scholarship: decimal.TryParse(cols[8].Trim(), out var sch) ? sch : 0,
                PaymentTotal: decimal.TryParse(cols[9].Trim(), out var tot) ? tot : 0,
                PCACodeLabel: cols[10].Trim(),
                HasTrueUpFlag: cols[11].Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)
            ));
        }
        return rows;
    }

    // ── IMPORT ────────────────────────────────────────────────────────────
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        // Validate sub-table
        if (string.IsNullOrWhiteSpace(TxtInvoiceNum.Text))
        {
            MessageBox.Show("Please enter an Invoice #.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!DateTime.TryParse(TxtStartDate.Text, out var startDate) ||
            !DateTime.TryParse(TxtEndDate.Text, out var endDate))
        {
            MessageBox.Show("Please enter valid Start and End dates.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime.TryParse(TxtPayDate.Text, out var payDate);
        int.TryParse(TxtClosureDays.Text, out var closureDays);

        var rows = ParsePasteData();
        if (rows.Count == 0)
        {
            MessageBox.Show("No data found in the paste area. " +
                "Please paste your MSDE data and try again.", "No Data",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var session = new ImportService.PaymentImportSession(
            TxtInvoiceNum.Text.Trim(), startDate, endDate,
            payDate == default ? null : payDate, closureDays);

        var svc = new ImportService(App.Db!, App.CurrentLocation!.LocationId);

        // Step 1 — Name validation
        StatusText.Text = "Validating...";
        StatusText.Visibility = Visibility.Visible;

        var nameErrors = await svc.ValidatePaymentNamesAsync(rows);
        if (nameErrors.Any())
        {
            StatusText.Visibility = Visibility.Collapsed;
            MessageBox.Show(
                $"Import rejected — {nameErrors.Count} name mismatch(es):\n\n" +
                string.Join("\n", nameErrors),
                "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Step 2 — Duplicate check
        var dupeCheck = await svc.CheckPaymentDuplicatesAsync(rows, session);
        if (dupeCheck.Duplicates.Any())
        {
            StatusText.Visibility = Visibility.Collapsed;
            var dlg = new DuplicateConfirmDialog(dupeCheck.Duplicates, "payment invoice");
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;
        }

        // Step 3 — Import
        StatusText.Text = "Importing...";
        var result = await svc.ImportPaymentsAsync(rows, session,
            App.CurrentUser!.UserId);

        // Clear on success
        PasteBox.Clear();
        TxtInvoiceNum.Clear();
        TxtStartDate.Clear();
        TxtEndDate.Clear();
        TxtPayDate.Clear();
        TxtClosureDays.Text = "0";
        StatusText.Visibility = Visibility.Collapsed;

        // Show notification
        ShowNotification(result);
    }

    // ── NOTIFICATION ──────────────────────────────────────────────────────
    private void ShowNotification(ImportService.ImportResult result)
    {
        bool hasIssues = result.RowsUnresolved > 0 || result.RowsSkipped > 0
                         || result.Errors.Any();

        NotificationBanner.Background = hasIssues
            ? new SolidColorBrush(Color.FromRgb(253, 235, 208))   // yellow
            : new SolidColorBrush(Color.FromRgb(213, 245, 227));  // green

        var msg = $"✓  {result.RowsImported} row(s) imported successfully.";
        if (result.RowsSkipped > 0)
            msg += $"  ·  {result.RowsSkipped} True-Up row(s) skipped.";
        if (result.RowsUnresolved > 0)
            msg += $"  ·  ⚠ {result.RowsUnresolved} unresolved voucher(s) — " +
                   "please review flagged entries on the child's profile.";
        if (result.Errors.Any())
            msg += $"  ·  {result.Errors.Count} error(s): " +
                   string.Join(", ", result.Errors);

        NotificationText.Text = msg;
        NotificationText.Foreground = hasIssues
            ? new SolidColorBrush(Color.FromRgb(126, 97, 6))
            : new SolidColorBrush(Color.FromRgb(30, 132, 73));

        NotificationBanner.Visibility = Visibility.Visible;

        // Auto-dismiss after 10 seconds
        _notifTimer?.Stop();
        _notifTimer = new DispatcherTimer
            { Interval = TimeSpan.FromSeconds(10) };
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

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        PasteBox.Clear();
        TxtInvoiceNum.Clear();
        TxtStartDate.Clear();
        TxtEndDate.Clear();
        TxtPayDate.Clear();
        TxtClosureDays.Text = "0";
        NotificationBanner.Visibility = Visibility.Collapsed;
    }
}
