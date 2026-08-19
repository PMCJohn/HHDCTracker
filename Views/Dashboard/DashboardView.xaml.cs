using HHDCTracker.Models;
using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        LocationHeader.Text = App.CurrentLocation?.Name ?? "Dashboard";
        DateHeader.Text = $"As of {DateTime.Today:dddd, MMMM d, yyyy}";
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var svc = new ProblemDetectionService(App.Db!, App.CurrentLocation!.LocationId);
        var summary = await svc.GetDashboardSummaryAsync();

        TotalOwedText.Text = $"${summary.TotalOwed:N2}";
        TotalProblemsText.Text = summary.TotalProblems.ToString();
        UnderpayText.Text = summary.OpenUnderpayments.ToString();
        CoverageGapText.Text = summary.CoverageGaps.ToString();
        PaymentGapText.Text = summary.PaymentGaps.ToString();
        LowPayText.Text = summary.LowPayments.ToString();

        // Expiring vouchers with computed days
        var expiring = summary.ExpiringVouchers
            .Select(v => new ExpiringVoucherRow(v))
            .ToList();

        if (expiring.Any())
        {
            ExpiringGrid.ItemsSource = expiring;
            ExpiringGrid.Visibility = Visibility.Visible;
            NoExpiringText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ExpiringGrid.Visibility = Visibility.Collapsed;
            NoExpiringText.Visibility = Visibility.Visible;
        }

        LastRefreshText.Text = $"Last refreshed @ {DateTime.Now:MM/dd/yyyy HH:mm}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();

    private record ExpiringVoucherRow(Voucher Voucher)
    {
        public Child? Child => Voucher.Child;
        public string VoucherNumber => Voucher.VoucherNumber;
        public DateTime? PeriodEnd => Voucher.PeriodEnd;
        public decimal VOPromisedWeekly => Voucher.VOPromisedWeekly;
        public int DaysUntilExpiry => Voucher.PeriodEnd.HasValue
            ? Math.Max(0, (Voucher.PeriodEnd.Value - DateTime.Today).Days) : 0;
        public bool IsUrgent => DaysUntilExpiry <= 7;
    }
}
