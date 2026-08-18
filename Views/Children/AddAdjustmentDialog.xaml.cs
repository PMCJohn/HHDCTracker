using HHDCTracker.Models;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Children;

public partial class AddAdjustmentDialog : Window
{
    private readonly int _childId;

    public AddAdjustmentDialog(int childId, List<Invoice> invoices)
    {
        InitializeComponent();
        _childId = childId;
        CboInvoice.ItemsSource = invoices.OrderByDescending(i => i.InvoiceStart).ToList();
        TxtDate.Text = DateTime.Today.ToString("MM/dd/yyyy");
        CboApplyTo.SelectedIndex = 0;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(TxtAmount.Text, out var amount))
        {
            MessageBox.Show("Please enter a valid amount.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!DateTime.TryParse(TxtDate.Text, out var date))
        {
            MessageBox.Show("Please enter a valid date.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var applyTo = (CboApplyTo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Balance";
        var invoice = CboInvoice.SelectedItem as Invoice;

        if ((applyTo == "Balance" || applyTo == "Balance from Credit") && invoice == null)
        {
            MessageBox.Show("Please select an invoice for Balance type adjustments.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.Db!.ManualAdjustments.Add(new ManualAdjustment
        {
            ChildId = _childId,
            InvoiceId = invoice?.InvoiceId,
            VoucherId = invoice?.VoucherId,
            AdjustmentDate = date,
            Amount = amount,
            ApplyTo = applyTo,
            Reason = TxtReason.Text.Trim(),
            CreatedByUserId = App.CurrentUser!.UserId
        });

        await App.Db.SaveChangesAsync();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
