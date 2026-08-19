using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HHDCTracker.Views.Import;

public partial class TrueUpImportView : UserControl
{
    private DispatcherTimer? _notifTimer;

    // Session-persistent state
    private static string _savedInvoiceNum = "";
    private static string _savedPasteData = "";
    private static int _savedTypeIndex = 0;

    public TrueUpImportView()
    {
        InitializeComponent();
        TxtInvoiceNum.Text = _savedInvoiceNum;
        PasteBox.Text = _savedPasteData;
        CboTrueUpType.SelectedIndex = _savedTypeIndex;

        TxtInvoiceNum.TextChanged += (_, _) => _savedInvoiceNum = TxtInvoiceNum.Text;
        PasteBox.TextChanged += (_, _) => _savedPasteData = PasteBox.Text;
        CboTrueUpType.SelectionChanged += (_, _) => _savedTypeIndex = CboTrueUpType.SelectedIndex;
    }

    private List<ImportService.TrueUpRow> ParsePasteData()
    {
        var rows = new List<ImportService.TrueUpRow>();
        var lines = PasteBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
        if (lines.Count > 0 && lines[0].ToLower().Contains("reason"))
            lines = lines.Skip(1).ToList();
        foreach (var line in lines)
        {
            var cols = line.Split('\t');
            if (cols.Length < 9) continue;
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
        var rows = ParsePasteData();
        if (rows.Count == 0)
        {
            MessageBox.Show("No data found in the paste area.", "No Data",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var session = new ImportService.TrueUpImportSession(
            string.IsNullOrEmpty(TxtInvoiceNum.Text.Trim()) ? null : TxtInvoiceNum.Text.Trim(),
            tuType);
        var svc = new ImportService(App.Db!, App.CurrentLocation!.LocationId);

        SetImportingState(true, "Validating...", 0, rows.Count);
        var nameErrors = await svc.ValidateTrueUpNamesAsync(rows);
        if (nameErrors.Any())
        {
            SetImportingState(false);
            MessageBox.Show(
                $"Import rejected — {nameErrors.Count} mismatch(es):\n\n" +
                string.Join("\n", nameErrors),
                "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dupeCheck = await svc.CheckTrueUpDuplicatesAsync(rows, session);
        if (dupeCheck.Duplicates.Any())
        {
            SetImportingState(false);
            var dlg = new DuplicateConfirmDialog(dupeCheck.Duplicates, "true-up");
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;
            SetImportingState(true, "Importing...", 0, rows.Count);
        }

        var result = await svc.ImportTrueUpsAsync(rows, session,
            App.CurrentUser!.UserId,
            (current, total) => Dispatcher.Invoke(() =>
                SetImportingState(true, $"Importing row {current} of {total}...",
                    current, total)));

        SetImportingState(false);
        PasteBox.Clear(); TxtInvoiceNum.Clear(); CboTrueUpType.SelectedIndex = 0;
        _savedInvoiceNum = _savedPasteData = ""; _savedTypeIndex = 0;

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
        var msg = $"✓  {result.RowsImported} true-up row(s) imported.";
        if (result.RowsUnresolved > 0)
            msg += $"  ·  ⚠ {result.RowsUnresolved} unresolved — review the Unresolved page.";
        if (result.Errors.Any())
            msg += $"  ·  {result.Errors.Count} error(s).";
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
        PasteBox.Clear(); TxtInvoiceNum.Clear(); CboTrueUpType.SelectedIndex = 0;
        _savedInvoiceNum = _savedPasteData = ""; _savedTypeIndex = 0;
        NotificationBanner.Visibility = Visibility.Collapsed;
    }
}
