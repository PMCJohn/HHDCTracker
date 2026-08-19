using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Dashboard;

public partial class ProblemReportView : UserControl
{
    private DateTime? _lastRefresh;
    private bool _showResolved = false;
    private ProblemDetectionService.ProblemSummary? _lastSummary;

    public ProblemReportView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var svc = new ProblemDetectionService(App.Db!, App.CurrentLocation!.LocationId);
        _lastSummary = await svc.DetectAllProblemsAsync();
        ApplyFilters();
        _lastRefresh = DateTime.Now;
        LastRefreshText.Text = $"Last refreshed @ {_lastRefresh:MM/dd/yyyy HH:mm}";
    }

    private void ApplyFilters()
    {
        if (_lastSummary == null) return;

        var underpayments = _showResolved
            ? _lastSummary.Underpayments
            : _lastSummary.Underpayments.Where(p => !p.IsResolved).ToList();

        UnderpayGrid.ItemsSource = underpayments;
        // Badge always shows open count only
        UnderpayCount.Text = _lastSummary.Underpayments.Count(p => !p.IsResolved).ToString();

        GapGrid.ItemsSource = _lastSummary.PaymentGaps;
        GapCount.Text = _lastSummary.PaymentGaps.Count.ToString();

        CovGrid.ItemsSource = _lastSummary.CoverageGaps;
        CovCount.Text = _lastSummary.CoverageGaps.Count.ToString();

        LowGrid.ItemsSource = _lastSummary.LowPayments;
        LowCount.Text = _lastSummary.LowPayments.Count.ToString();

        ShowResolvedBtn.Content = _showResolved
            ? "Hide Resolved" : "Show Resolved";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();

    private void ShowResolved_Click(object sender, RoutedEventArgs e)
    {
        _showResolved = !_showResolved;
        ApplyFilters();
    }
}
