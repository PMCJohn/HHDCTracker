using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace HHDCTracker.Views.Dashboard;

public partial class UserEditDialog : Window
{
    private readonly User _user;
    private List<Location> _allLocations = [];

    public UserEditDialog(User user)
    {
        InitializeComponent();
        _user = user;
        TitleText.Text = $"Edit User — {user.DisplayName}";
        TxtName.Text = user.DisplayName;
        CboRole.SelectedIndex = user.Role == "Admin" ? 1 : 0;
        ChkActive.IsChecked = user.IsActive;
        ChkArchived.IsChecked = user.IsArchived;
        Loaded += async (_, _) => await LoadLocationsAsync();
    }

    private async Task LoadLocationsAsync()
    {
        _allLocations = await App.Db!.Locations
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync();

        var assignedIds = _user.UserLocations.Select(ul => ul.LocationId).ToHashSet();

        LocationChecks.ItemsSource = _allLocations.Select(l => new LocationCheckItem
        {
            Location = l,
            IsAssigned = assignedIds.Contains(l.LocationId)
        }).ToList();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Display name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _user.DisplayName = name;
        _user.Role = (CboRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Staff";
        _user.IsActive = ChkActive.IsChecked == true;
        _user.IsArchived = ChkArchived.IsChecked == true;

        // If archived, also deactivate
        if (_user.IsArchived) _user.IsActive = false;

        // Update location assignments
        var existing = _user.UserLocations.ToList();
        App.Db!.UserLocations.RemoveRange(existing);

        var checkedItems = (LocationChecks.ItemsSource as List<LocationCheckItem>)
            ?.Where(i => i.IsAssigned).ToList() ?? [];

        foreach (var item in checkedItems)
        {
            App.Db.UserLocations.Add(new UserLocation
            {
                UserId = _user.UserId,
                LocationId = item.Location.LocationId
            });
        }

        await App.Db.SaveChangesAsync();
        DialogResult = true;
        Close();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Are you sure you want to delete '{_user.DisplayName}'?\n\n" +
            "Consider archiving instead to preserve audit history.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // Null out foreign key references then delete
        var locks = await App.Db!.RecordLocks
            .Where(l => l.LockedByUserId == _user.UserId).ToListAsync();
        App.Db.RecordLocks.RemoveRange(locks);

        App.Db.Users.Remove(_user);
        await App.Db.SaveChangesAsync();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private class LocationCheckItem
    {
        public Location Location { get; set; } = null!;
        public bool IsAssigned { get; set; }
        public string Name => Location.Name;
    }
}
