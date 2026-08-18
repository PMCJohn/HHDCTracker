using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views;

public partial class LoginWindow : Window
{
    private List<User> _users = [];

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _users = await App.Db!.Users
            .Include(u => u.UserLocations)
            .ThenInclude(ul => ul.Location)
            .Where(u => u.IsActive && !u.IsArchived)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        UserCombo.ItemsSource = _users;
        if (_users.Count > 0) UserCombo.SelectedIndex = 0;
    }

    private void UserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserCombo.SelectedItem is not User user) return;

        // Admins see all locations; staff see only assigned ones
        List<Location> locations;
        if (user.Role == "Admin")
        {
            locations = App.Db!.Locations
                .Where(l => l.IsActive)
                .OrderBy(l => l.Name)
                .ToList();
        }
        else
        {
            locations = user.UserLocations
                .Where(ul => ul.Location != null)
                .Select(ul => ul.Location!)
                .OrderBy(l => l.Name)
                .ToList();
        }

        LocationCombo.ItemsSource = locations;
        LocationCombo.IsEnabled = locations.Count > 1 || user.Role == "Admin";

        // Pre-select last used location
        if (user.LastUsedLocationId.HasValue)
        {
            var last = locations.FirstOrDefault(l => l.LocationId == user.LastUsedLocationId);
            LocationCombo.SelectedItem = last ?? locations.FirstOrDefault();
        }
        else
        {
            LocationCombo.SelectedIndex = 0;
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (UserCombo.SelectedItem is not User user)
        {
            ErrorText.Text = "Please select a user.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (LocationCombo.SelectedItem is not Location loc)
        {
            ErrorText.Text = "Please select a location.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Save last used location
        user.LastUsedLocationId = loc.LocationId;
        await App.Db!.SaveChangesAsync();

        App.CurrentUser = user;
        App.CurrentLocation = loc;

        new MainWindow().Show();
        Close();
    }
}
