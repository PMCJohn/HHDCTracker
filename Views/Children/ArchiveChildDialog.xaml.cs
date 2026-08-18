using HHDCTracker.Models;
using System.Windows;

namespace HHDCTracker.Views.Children;

public partial class ArchiveChildDialog : Window
{
    private readonly Child _child;
    private readonly Voucher? _activeVoucher;

    public bool TerminateVoucher { get; private set; }
    public DateTime? TerminationDate { get; private set; }

    public ArchiveChildDialog(Child child, Voucher? activeVoucher)
    {
        InitializeComponent();
        _child = child;
        _activeVoucher = activeVoucher;

        TitleText.Text = $"Archive {child.FullName}?";
        TxtTermDate.Text = DateTime.Today.ToString("MM/dd/yyyy");

        if (activeVoucher != null)
        {
            VoucherPanel.Visibility = Visibility.Visible;
            VoucherNumRun.Text = activeVoucher.VoucherNumber;
        }
    }

    private void TerminateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (TermDatePanel == null) return;
        TermDatePanel.Visibility = TerminateVoucherCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        TerminateVoucher = _activeVoucher != null &&
                           TerminateVoucherCheck.IsChecked == true;

        if (TerminateVoucher)
        {
            if (!DateTime.TryParse(TxtTermDate.Text, out var termDate))
            {
                MessageBox.Show("Please enter a valid termination date.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TerminationDate = termDate;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
