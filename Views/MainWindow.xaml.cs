using HHDCTracker.Models;
using HHDCTracker.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HHDCTracker.Views;

public partial class MainWindow : Window
{
    private List<Child> _allChildren = [];
    private bool _showArchived = false;
    private static readonly HashSet<string> ChildPanelViews = new() { "Children" };

    public MainWindow()
    {
        InitializeComponent();
        ProgressService.ProgressChanged += OnProgressChanged;
        ProgressService.ProgressCompleted += OnProgressCompleted;
        Closed += (_, _) =>
        {
            ProgressService.ProgressChanged -= OnProgressChanged;
            ProgressService.ProgressCompleted -= OnProgressCompleted;
        };
        Loaded += async (_, _) => await InitAsync();
    }

    private void OnProgressChanged(double percent, string description)
    {
        ProgressArea.Visibility = Visibility.Visible;
        NavProgressBar.Value = percent;
        ProgressLabel.Text = description;
        ProgressPct.Text = $"{percent:N0}%";
    }

    private void OnProgressCompleted()
    {
        ProgressArea.Visibility = Visibility.Collapsed;
        NavProgressBar.Value = 0;
    }

    private async Task InitAsync()
    {
        CurrentUserText.Text = App.CurrentUser?.DisplayName ?? "";
        LocationText.Text = $"📍 {App.CurrentLocation?.Name ?? ""}";
        await LoadChildrenAsync();
        ShowDetailView("Dashboard");
    }

    // ── Child List ────────────────────────────────────────────────────────
    public async Task LoadChildrenAsync()
    {
        _allChildren = await DbRetryService.ExecuteAsync(() =>
            App.Db!.Children.AsNoTracking()
                .Include(c => c.Vouchers)
                .Where(c => c.LocationId == App.CurrentLocation!.LocationId)
                .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
                .ToListAsync());
        ApplyChildFilter();
    }

    private void ApplyChildFilter()
    {
        var term = SearchBox.Text.Trim().ToLower();
        var filtered = _allChildren
            .Where(c => _showArchived ? c.IsArchived : !c.IsArchived)
            .Where(c => string.IsNullOrEmpty(term) ||
                c.FullName.ToLower().Contains(term) ||
                c.Vouchers.Any(v => v.VoucherNumber.Contains(term)))
            .ToList();
        ChildList.ItemsSource = filtered;
    }

    public void OpenChildProfile(int childId)
    {
        var child = _allChildren.FirstOrDefault(c => c.ChildId == childId);
        if (child != null) ChildList.SelectedItem = child;
        ShowChildPane(true);
        DetailPane.Content = new Children.ChildProfileView(childId, this);
    }

    private void ShowChildPane(bool visible)
    {
        ChildPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ChildPaneCol.Width = visible ? new GridLength(280) : new GridLength(0);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyChildFilter();
    }

    private void ArchiveToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showArchived = ShowArchivedToggle.IsChecked == true;
        ApplyChildFilter();
    }

    private void ChildList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChildList.SelectedItem is Child child)
            DetailPane.Content = new Children.ChildProfileView(child.ChildId, this);
    }

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        ChildList.SelectedItem = null;
        DetailPane.Content = new Children.ChildProfileView(null, this);
    }

    // ── Navigation ────────────────────────────────────────────────────────
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked) return;
        var tag = clicked.Tag?.ToString() ?? "";
        foreach (var btn in new[] { NavDashboard, NavChildren, NavProblems,
                                     NavUnresolved, NavImport, NavTrueUp, NavSettings })
            btn.Style = (Style)Resources["NavButton"];
        clicked.Style = (Style)Resources["NavButtonActive"];
        ShowDetailView(tag);
    }

    private void ShowDetailView(string tag)
    {
        bool showChildren = ChildPanelViews.Contains(tag);
        ShowChildPane(showChildren);
        if (!showChildren) ChildList.SelectedItem = null;

        DetailPane.Content = tag switch
        {
            "Dashboard"  => new Dashboard.DashboardView(),
            "Problems"   => new Dashboard.ProblemReportView(),
            "Unresolved" => new Dashboard.UnresolvedView(),
            "Import"     => new Import.PaymentImportView(),
            "TrueUp"     => new Import.TrueUpImportView(),
            "Settings"   => new Dashboard.SettingsView(),
            "Children"   => null,
            _            => null
        };
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        ProgressService.ProgressChanged -= OnProgressChanged;
        ProgressService.ProgressCompleted -= OnProgressCompleted;
        App.CurrentUser = null;
        App.CurrentLocation = null;
        new LoginWindow().Show();
        Close();
    }
}
