using HHDCTracker.Models;
using System.Windows;

namespace HHDCTracker.Views.Dashboard;

public partial class LocationEditDialog : Window
{
    private readonly Location _location;

    public LocationEditDialog(Location location)
    {
        InitializeComponent();
        _location = location;
        TitleText.Text = $"Edit Location — {location.Name}";
        TxtName.Text = location.Name;
        TxtAddress.Text = location.Address ?? "";
        ChkActive.IsChecked = location.IsActive;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Location name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _location.Name = name;
        _location.Address = TxtAddress.Text.Trim();
        _location.IsActive = ChkActive.IsChecked == true;

        await App.Db!.SaveChangesAsync();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
