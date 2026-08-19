using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HHDCTracker.Views.Import;

public partial class PaymentImportView : UserControl
{
    private DispatcherTimer? _notifTimer;

    // Session-persistent form state — survives navigation, cleared on successful import
    private static string _savedInvoiceNum = "";
    private static string _savedStartDate = "";
    private static string _savedEndDate = "";
    private static string _savedPayDate = "";
    private static string _savedPasteData = "";

    public PaymentImportView()
    {
        InitializeComponent();
        // Restore saved state
        TxtInvoiceNum.Text = _savedInvoiceNum;
        TxtStartDate.Text = _savedStartDate;
        TxtEndDate.Text = _savedEndDate;
        TxtPayDate.Text = _savedPayDate;
        PasteBox.Text = _savedPasteData;

        // Save state as user types
        TxtInvoiceNum.TextChanged += (_, _) => _savedInvoiceNum = TxtInvoiceNum.Text;
        TxtStartDate.TextChanged += (_, _) => _savedStartDate = TxtStartDate.Text;
        TxtEndDate.TextChanged += (_, _) => _savedEndDate = TxtEndDate.Text;
        TxtPayDate.TextChanged += (_, _) => _savedPayDate = TxtPayDate.Text;
        PasteBox.TextChanged += (_, _) => _savedPasteData = PasteBox.Text;
    }

    private List<ImportService.PaymentRow> ParsePasteData()
    {
        var rows = new List<ImportService.PaymentRow>();
        var lines = PasteBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        if (lines.Count > 0 && lines[0].ToLower().Contains("child name"))
            lines = lines.Skip(1).ToList();

        foreach (var line in lines)
        {
            var cols = line.Split('\t');
            if (cols.Length < 13) continue;
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

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
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
        var rows = ParsePasteData();
        if (rows.Count == 0)
        {
            MessageBox.Show("No data found in the paste area.", "No Data",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var session = new ImportService.PaymentImportSession(
            TxtInvoiceNum.Text.Trim(), startDate, endDate,
            payDate == default ? null : payDate);

        var svc = new ImportService(App.Db!, App.CurrentLocation!.LocationId);

        // Validate
        SetImportingState(true, "Validating...", 0, rows.Count);
        var nameErrors = await svc.ValidatePaymentNamesAsync(rows);
        if (nameErrors.Any())
        {
            SetImportingState(false);
            MessageBox.Show(
                $"Import rejected — {nameErrors.Count} mismatch(es):\n\n" +
                string.Join("\n", nameErrors),
                "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Duplicate check
        var dupeCheck = await svc.CheckPaymentDuplicatesAsync(rows, session);
        if (dupeCheck.Duplicates.Any())
        {
            SetImportingState(false);
            var dlg = new DuplicateConfirmDialog(dupeCheck.Duplicates, "payment invoice");
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;
            SetImportingState(true, "Importing...", 0, rows.Count);
        }

        // Import with progress
        var result = await svc.ImportPaymentsAsync(rows, session,
            App.CurrentUser!.UserId,
            (current, total) => Dispatcher.Invoke(() =>
                SetImportingState(true, $"Importing row {current} of {total}...",
                    current, total)));

        SetImportingState(false);

        // Clear form and saved state on success
        PasteBox.Clear(); TxtInvoiceNum.Clear(); TxtStartDate.Clear();
        TxtEndDate.Clear(); TxtPayDate.Clear();
        _savedInvoiceNum = _savedStartDate = _savedEndDate =
            _savedPayDate = _savedPasteData = "";

        ShowNotification(result);
    }

    private void SetImportingState(bool importing, string label = "",
        int current = 0, int total = 1)
    {
        BtnImport.IsEnabled = !importing;
        ProgressPanel.Visibility = importing ? Visibility.Visible : Visibility.Collapsed;
        if (!importing) return;
        ProgressLabel.Text = label;
        double pct = total > 0 ? (double)current / total * 100 : 0;
        ImportProgressBar.Value = pct;
        ProgressPct.Text = $"{pct:N0}%";
    }

    private void ShowNotification(ImportService.ImportResult result)
    {
        bool hasIssues = result.RowsUnresolved > 0 || result.Errors.Any();
        NotificationBanner.Background = hasIssues
            ? new SolidColorBrush(Color.FromRgb(253, 235, 208))
            : new SolidColorBrush(Color.FromRgb(213, 245, 227));

        var msg = $"✓  {result.RowsImported} row(s) imported successfully.";
        if (result.RowsSkipped > 0)
            msg += $"  ·  {result.RowsSkipped} True-Up row(s) skipped.";
        if (result.RowsUnresolved > 0)
            msg += $"  ·  ⚠ {result.RowsUnresolved} unresolved — please review the Unresolved page.";
        if (result.Errors.Any())
            msg += $"  ·  {result.Errors.Count} error(s): {string.Join(", ", result.Errors)}";

        NotificationText.Text = msg;
        NotificationText.Foreground = hasIssues
            ? new SolidColorBrush(Color.FromRgb(126, 97, 6))
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

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        PasteBox.Clear(); TxtInvoiceNum.Clear(); TxtStartDate.Clear();
        TxtEndDate.Clear(); TxtPayDate.Clear();
        _savedInvoiceNum = _savedStartDate = _savedEndDate =
            _savedPayDate = _savedPasteData = "";
        NotificationBanner.Visibility = Visibility.Collapsed;
    }
}
