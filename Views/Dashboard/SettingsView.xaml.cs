using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HHDCTracker.Views.Dashboard;

public partial class SettingsView : UserControl
{
    private bool _isAdmin => App.CurrentUser?.Role == "Admin";

    public SettingsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private bool _showArchivedUsers = false;

    private async Task LoadAsync()
    {
        TxtCurrentPath.Text = App.DbPath;
        TxtNewPath.Text = App.DbPath;

        var fi = new FileInfo(App.DbPath);
        TxtDbInfo.Text = fi.Exists
            ? $"File: {fi.Length / 1024.0:N1} KB  ·  Modified: {fi.LastWriteTime:MM/dd/yyyy HH:mm}"
            : "Database file not found.";

        var users = await App.Db!.Users
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Where(u => _showArchivedUsers ? u.IsArchived : !u.IsArchived)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
        UserItemsControl.ItemsSource = users;

        var locs = await App.Db.Locations.OrderBy(l => l.Name).ToListAsync();
        LocationItemsControl.ItemsSource = locs;
    }

    private void ArchivedUsersToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showArchivedUsers = ShowArchivedUsersToggle.IsChecked == true;
        _ = LoadAsync();
    }

    // ── DB PATH ───────────────────────────────────────────────────────────
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select database file",
            Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
            CheckFileExists = false
        };
        if (dlg.ShowDialog() == true) TxtNewPath.Text = dlg.FileName;
    }

    private void ApplyPath_Click(object sender, RoutedEventArgs e)
    {
        var newPath = TxtNewPath.Text.Trim();
        if (string.IsNullOrEmpty(newPath)) return;

        var result = MessageBox.Show(
            $"Point the app to:\n\n{newPath}\n\nThe app will restart. Continue?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var configPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "hhdc.config");
        File.WriteAllText(configPath, newPath);

        System.Diagnostics.Process.Start(
            System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
        Application.Current.Shutdown();
    }

    // ── USERS ─────────────────────────────────────────────────────────────
    private void UserRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isAdmin) return;
        if (sender is FrameworkElement fe && fe.DataContext is User user)
        {
            var dlg = new UserEditDialog(user);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true) _ = LoadAsync();
        }
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin)
        {
            MessageBox.Show("Only admins can add users.", "Permission Denied",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var name = TxtNewUserName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a display name.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var role = (CboNewUserRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Staff";
        var user = new User { DisplayName = name, Role = role };
        App.Db!.Users.Add(user);
        await App.Db.SaveChangesAsync();

        // Assign to current location by default
        App.Db.UserLocations.Add(new UserLocation
        {
            UserId = user.UserId,
            LocationId = App.CurrentLocation!.LocationId
        });
        await App.Db.SaveChangesAsync();

        TxtNewUserName.Text = "";
        UserStatus.Text = $"User '{name}' added.";
        UserStatus.Visibility = Visibility.Visible;
        await LoadAsync();
    }

    // ── LOCATIONS ─────────────────────────────────────────────────────────
    private void LocationRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isAdmin) return;
        if (sender is FrameworkElement fe && fe.DataContext is Location loc)
        {
            var dlg = new LocationEditDialog(loc);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true) _ = LoadAsync();
        }
    }

    // ──
    private async void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdmin)
        {
            MessageBox.Show("Only admins can add locations.", "Permission Denied",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var name = TxtNewLocName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a location name.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Db!.Locations.Add(new Location
        {
            Name = name,
            Address = TxtNewLocAddress.Text.Trim()
        });
        await App.Db.SaveChangesAsync();
        TxtNewLocName.Text = "";
        TxtNewLocAddress.Text = "";
        LocStatus.Text = $"Location '{name}' added.";
        LocStatus.Visibility = Visibility.Visible;
        await LoadAsync();
    }
}
