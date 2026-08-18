using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Dashboard;

public partial class ProblemReportView : UserControl
{
    private DateTime? _lastRefresh;

    public ProblemReportView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var svc = new ProblemDetectionService(App.Db!, App.CurrentLocation!.LocationId);
        var summary = await svc.DetectAllProblemsAsync();

        UnderpayGrid.ItemsSource = summary.Underpayments;
        UnderpayCount.Text = summary.Underpayments.Count(p => !p.IsResolved).ToString();

        GapGrid.ItemsSource = summary.PaymentGaps;
        GapCount.Text = summary.PaymentGaps.Count.ToString();

        CovGrid.ItemsSource = summary.CoverageGaps;
        CovCount.Text = summary.CoverageGaps.Count.ToString();

        LowGrid.ItemsSource = summary.LowPayments;
        LowCount.Text = summary.LowPayments.Count.ToString();

        _lastRefresh = DateTime.Now;
        LastRefreshText.Text = $"Last refreshed @ {_lastRefresh:MM/dd/yyyy HH:mm}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();
}
