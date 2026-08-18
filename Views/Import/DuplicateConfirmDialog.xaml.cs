using System.Windows;

namespace HHDCTracker.Views.Import;

public partial class DuplicateConfirmDialog : Window
{
    public DuplicateConfirmDialog(List<string> duplicates, string importType)
    {
        InitializeComponent();
        TitleText.Text = $"{duplicates.Count} Duplicate(s) Found";
        SubText.Text = $"The following {importType} entries already exist in the ledger. " +
                       "Proceeding will skip these rows. Cancel to abort the entire import.";
        DupeList.ItemsSource = duplicates;
    }

    private void Proceed_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
