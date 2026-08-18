using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HHDCTracker.Views.Import;

public partial class TrueUpImportView : UserControl
{
    private DispatcherTimer? _notifTimer;

    public TrueUpImportView()
    {
        InitializeComponent();
    }

    private List<ImportService.TrueUpRow> ParsePasteData()
    {
        var rows = new List<ImportService.TrueUpRow>();
        var lines = PasteBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        // Skip header row if present
        if (lines.Count > 0 && lines[0].ToLower().Contains("reason"))
            lines = lines.Skip(1).ToList();

        foreach (var line in lines)
        {
            var cols = line.Split('\t');
            if (cols.Length < 9) continue;

            // Col map: 0=Reason, 1=ScholarshipNumber, 2=ChildName,
            //          3=1stAPInvoice, 4=2ndAPInvoice, 5=APMonth,
            //          6=APAmount, 7=AdjustDays, 8=TrueUpAdjustAmount
            rows.Add(new ImportService.TrueUpRow(
                Reason: cols[0].Trim(),
                ScholarshipNumber: cols[1].Trim(),
                ChildNameMSDE: cols[2].Trim(),
                FirstAPInvoice: cols[3].Trim(),
                SecondAPInvoice: cols[4].Trim(),
                APReconcilingMonth: cols[5].Trim(),
                APAmount: decimal.TryParse(cols[6].Trim(), out var ap) ? ap : 0,
                AdjustDays: int.TryParse(cols[7].Trim(), out var adj) ? adj : 0,
                TrueUpAdjustAmount: decimal.TryParse(cols[8].Trim(), out var amt) ? amt : 0
            ));
        }
        return rows;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var tuType = (CboTrueUpType.SelectedItem as ComboBoxItem)?.Content?.ToString()
                     ?? "Additional Amounts Reconciliation";
        var invoiceNum = TxtInvoiceNum.Text.Trim();

        var rows = ParsePasteData();
        if (rows.Count == 0)
        {
            MessageBox.Show("No data found in the paste area. " +
                "Please paste your MSDE true-up data and try again.", "No Data",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var session = new ImportService.TrueUpImportSession(
            string.IsNullOrEmpty(invoiceNum) ? null : invoiceNum, tuType);

        var svc = new ImportService(App.Db!, App.CurrentLocation!.LocationId);

        // Step 1 — Name validation
        StatusText.Text = "Validating...";
        StatusText.Visibility = Visibility.Visible;

        var nameErrors = await svc.ValidateTrueUpNamesAsync(rows);
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
        var dupeCheck = await svc.CheckTrueUpDuplicatesAsync(rows, session);
        if (dupeCheck.Duplicates.Any())
        {
            StatusText.Visibility = Visibility.Collapsed;
            var dlg = new DuplicateConfirmDialog(dupeCheck.Duplicates, "true-up");
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;
        }

        // Step 3 — Import
        StatusText.Text = "Importing...";
        var result = await svc.ImportTrueUpsAsync(rows, session,
            App.CurrentUser!.UserId);

        // Clear on success
        PasteBox.Clear();
        TxtInvoiceNum.Clear();
        CboTrueUpType.SelectedIndex = 0;
        StatusText.Visibility = Visibility.Collapsed;

        ShowNotification(result);
    }

    private void ShowNotification(ImportService.ImportResult result)
    {
        bool hasIssues = result.RowsUnresolved > 0 || result.Errors.Any();

        NotificationBanner.Background = hasIssues
            ? new SolidColorBrush(Color.FromRgb(253, 235, 208))
            : new SolidColorBrush(Color.FromRgb(213, 245, 227));

        var msg = $"✓  {result.RowsImported} true-up row(s) imported successfully.";
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
        CboTrueUpType.SelectedIndex = 0;
        NotificationBanner.Visibility = Visibility.Collapsed;
    }
}
