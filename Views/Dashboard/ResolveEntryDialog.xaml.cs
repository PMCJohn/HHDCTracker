using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Dashboard;

public partial class ResolveEntryDialog : Window
{
    private readonly string _rawVoucherNumber;

    public Child? SelectedChild { get; private set; }
    public Voucher? SelectedVoucher { get; private set; }

    public ResolveEntryDialog(string rawChildName, string rawVoucherNumber,
        string entryType, string invoiceRef, string entryDetails)
    {
        InitializeComponent();
        _rawVoucherNumber = rawVoucherNumber;

        TitleText.Text = $"Resolve Unresolved {entryType}";
        LblRawName.Text = rawChildName;
        LblRawVoucher.Text = rawVoucherNumber;
        LblEntryDetails.Text = entryDetails;

        // Pre-fill search with MSDE name
        TxtSearch.Text = rawChildName;
        Loaded += async (_, _) => await SearchAsync(rawChildName);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
        => await SearchAsync(TxtSearch.Text.Trim());

    private async Task SearchAsync(string term)
    {
        if (string.IsNullOrEmpty(term)) return;

        var results = await App.Db!.Children
            .Include(c => c.Vouchers)
            .Include(c => c.Aliases)
            .Where(c => c.LocationId == App.CurrentLocation!.LocationId
                && (c.FirstName.Contains(term) || c.LastName.Contains(term)
                    || (c.FirstName + " " + c.LastName).Contains(term)
                    || c.Aliases.Any(a => a.AliasName.Contains(term))))
            .OrderBy(c => c.LastName)
            .Take(20)
            .ToListAsync();

        ResultsList.ItemsSource = results;
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not Child child)
        {
            SelectionPanel.Visibility = Visibility.Collapsed;
            BtnResolve.IsEnabled = false;
            return;
        }

        SelectedChild = child;
        LblSelectedChild.Text = child.FullName;

        // Load vouchers for this child — highlight the one matching the raw voucher number
        var vouchers = child.Vouchers
            .OrderByDescending(v => v.PeriodStart)
            .Select(v => new VoucherDisplayItem(v))
            .ToList();

        VoucherCombo.ItemsSource = vouchers;

        // Pre-select matching voucher number
        var match = vouchers.FirstOrDefault(v =>
            v.Voucher.VoucherNumber == _rawVoucherNumber);
        VoucherCombo.SelectedItem = match ?? vouchers.FirstOrDefault();

        SelectionPanel.Visibility = Visibility.Visible;
        BtnResolve.IsEnabled = VoucherCombo.SelectedItem != null;
    }

    private void VoucherCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedVoucher = (VoucherCombo.SelectedItem as VoucherDisplayItem)?.Voucher;
        BtnResolve.IsEnabled = SelectedChild != null && SelectedVoucher != null;
    }

    private void Resolve_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedChild == null || SelectedVoucher == null) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private record VoucherDisplayItem(Voucher Voucher)
    {
        public string VoucherDisplay =>
            $"{Voucher.VoucherNumber}  ({Voucher.PeriodStart:MM/dd/yyyy} → " +
            $"{(Voucher.PeriodEnd.HasValue ? Voucher.PeriodEnd.Value.ToString("MM/dd/yyyy") : "ongoing")})";
    }
}
