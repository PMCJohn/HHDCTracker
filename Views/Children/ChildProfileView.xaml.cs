using HHDCTracker.Models;
using HHDCTracker.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HHDCTracker.Views.Children;

public partial class ChildProfileView : UserControl
{
    private int? _childId;
    private Child? _child;
    private bool _isEditingChild = false;
    private readonly LockService _lockService;
    private readonly MainWindow? _mainWindow;
    private readonly BalanceService _balanceService;
    // Persists the selected tab across child profile switches
    private static int _lastTabIndex = 0;

    public ChildProfileView(int? childId, MainWindow? mainWindow = null)
    {
        InitializeComponent();
        _childId = childId;
        _lockService = new LockService(App.Db!, App.CurrentUser!.UserId);
        _mainWindow = mainWindow;
        _balanceService = new BalanceService(App.Db!);
        Loaded += async (_, _) => await LoadAsync();
    }

    // ── LOAD ──────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        if (_childId == null)
        {
            // New child mode — start in edit mode immediately
            ChildNameHeader.Text = "New Child";
            ChildSubHeader.Text = "Fill in the details below";
            EnterEditMode();
            return;
        }

        _child = await App.Db!.Children
            .Include(c => c.Aliases)
            .Include(c => c.Vouchers.OrderByDescending(v => v.PeriodStart))
            .Include(c => c.Invoices.OrderByDescending(i => i.InvoiceStart))
            .Include(c => c.TrueUps.OrderByDescending(t => t.ImportedAt))
            .Include(c => c.ManualAdjustments.OrderByDescending(a => a.AdjustmentDate))
            .FirstOrDefaultAsync(c => c.ChildId == _childId);

        if (_child == null) return;

        PopulateHeader();
        PopulateOverview();
        PopulateVoucherList();
        PopulateGrids();
        await PopulateBalancesAsync();
        await PopulateProblemsAsync();

        // Restore last selected tab without triggering SelectionChanged save
        if (_lastTabIndex < ProfileTabs.Items.Count)
            ProfileTabs.SelectedIndex = _lastTabIndex;
    }

    private static string CalculateAge(DateTime? dob)
    {
        if (!dob.HasValue) return "";
        var today = DateTime.Today;
        int years = today.Year - dob.Value.Year;
        int months = today.Month - dob.Value.Month;
        if (today.Day < dob.Value.Day) months--;
        if (months < 0) { years--; months += 12; }
        return years > 0
            ? $"{years}y {months}m"
            : $"{months}m";
    }

    private void PopulateHeader()
    {
        ChildNameHeader.Text = _child!.FullName;
        var archiveLabel = _child.IsArchived
            ? $"  ·  ARCHIVED {_child.ArchivedAt:MM/dd/yyyy}" : "";
        var age = _child.DateOfBirth.HasValue
            ? $"  ·  Age: {CalculateAge(_child.DateOfBirth)}" : "";
        ChildSubHeader.Text = $"DOB: {_child.DateOfBirth:MM/dd/yyyy}{age}  ·  " +
                              $"{_child.Vouchers.Count} voucher(s){archiveLabel}";
        UnresolvedBanner.Visibility = _child.HasUnresolved
            ? Visibility.Visible : Visibility.Collapsed;
        BtnArchive.Visibility = (!_child.IsArchived)
            ? Visibility.Visible : Visibility.Collapsed;
        BtnUnarchive.Visibility = _child.IsArchived
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateOverview()
    {
        LblFirstName.Text = _child!.FirstName;
        LblLastName.Text = _child.LastName;
        LblDOB.Text = _child.DateOfBirth?.ToString("MM/dd/yyyy") ?? "—";
        LblNotes.Text = string.IsNullOrWhiteSpace(_child.Notes) ? "—" : _child.Notes;
        AliasPanel.ItemsSource = _child.Aliases.ToList();

        // Active voucher snapshot
        var active = _child.Vouchers.FirstOrDefault(v => v.IsActive);
        if (active != null)
        {
            OvVoucherNum.Text = active.VoucherNumber;
            OvRateType.Text = active.RateType;
            OvPeriod.Text = $"{active.PeriodStart:MM/dd/yyyy} → " +
                            (active.PeriodEnd.HasValue
                                ? active.PeriodEnd.Value.ToString("MM/dd/yyyy")
                                : "ongoing");
            OvVO.Text = $"${active.VOPromisedWeekly:N2}";
            OvHHDC.Text = $"${active.HHDCChargeWeekly:N2}";

            var copay = active.ExpectedWeeklyCopay;
            OvCopay.Text = $"${copay:N2}";
            OvCopay.Foreground = copay < 0
                ? new SolidColorBrush(Color.FromRgb(39, 174, 96))   // green — VO covers more
                : new SolidColorBrush(Color.FromRgb(26, 82, 118));   // teal — normal copay

            OvSummer.Text = active.SummerRateStart.HasValue
                ? $"{active.SummerRateStart:MM/dd} – {active.SummerRateEnd:MM/dd} " +
                  $"(VO: ${active.VOSummerWeekly:N2} / HHDC: ${active.HHDCSummerWeekly:N2})"
                : "—";

            ActiveVoucherRow.Visibility = Visibility.Visible;
            NoActiveVoucher.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActiveVoucherRow.Visibility = Visibility.Collapsed;
            NoActiveVoucher.Visibility = Visibility.Visible;
        }
    }

    private async Task PopulateBalancesAsync()
    {
        if (_child == null) return;
        var summary = await _balanceService.GetBalanceSummaryAsync(_child.ChildId);

        SetBalanceLabel(BalLedger, summary.LedgerBalance);
        SetBalanceLabel(BalTrueUp, summary.TrueUpBalance);
        SetBalanceLabel(BalNet, summary.NetBalance);

        // Running credit — green if positive, red if negative
        BalCredit.Text = $"${summary.RunningCredit:N2}";
        BalCredit.Foreground = summary.RunningCredit < 0
            ? new SolidColorBrush(Color.FromRgb(231, 76, 60))
            : new SolidColorBrush(Color.FromRgb(39, 174, 96));

        // Net balance card background
        NetBalCard.Background = summary.NetBalance < 0
            ? new SolidColorBrush(Color.FromRgb(250, 219, 216))
            : summary.NetBalance > 0
                ? new SolidColorBrush(Color.FromRgb(213, 245, 227))
                : new SolidColorBrush(Color.FromRgb(248, 249, 250));
    }

    private static void SetBalanceLabel(TextBlock lbl, decimal value)
    {
        lbl.Text = $"${value:N2}";
        lbl.Foreground = value < 0
            ? new SolidColorBrush(Color.FromRgb(231, 76, 60))
            : value > 0
                ? new SolidColorBrush(Color.FromRgb(39, 174, 96))
                : new SolidColorBrush(Color.FromRgb(100, 100, 100));
    }

    private async Task PopulateProblemsAsync()
    {
        if (_child == null) return;
        var problems = await _balanceService.GetOpenProblemsAsync(_child.ChildId);
        var open = problems.Where(p => !p.IsResolved).ToList();

        if (open.Any())
        {
            ProblemsList.ItemsSource = open;
            ProblemsCard.Visibility = Visibility.Visible;
        }
        else
        {
            ProblemsCard.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateGrids()
    {
        if (_child == null) return;
        InvoiceGrid.ItemsSource = _child.Invoices.ToList();
        TrueUpGrid.ItemsSource = _child.TrueUps.ToList();

        // Build adjustment list with running credit balance per row
        decimal runningCredit = 0;
        var adjRows = _child.ManualAdjustments
            .OrderBy(a => a.AdjustmentDate)
            .Select(a =>
            {
                if (a.ApplyTo == "Credit") runningCredit += a.Amount;
                else if (a.ApplyTo == "Balance from Credit") runningCredit -= a.Amount;
                return new AdjRowVm(a, runningCredit);
            }).ToList();
        AdjGrid.ItemsSource = adjRows;
    }

    // ── VOUCHER LIST ──────────────────────────────────────────────────────
    private void PopulateVoucherList()
    {
        VoucherList.Children.Clear();
        if (_child == null) return;

        foreach (var voucher in _child.Vouchers.OrderByDescending(v => v.PeriodStart))
            VoucherList.Children.Add(new VoucherRowControl(voucher, OnVoucherSaved));
    }

    private async void OnVoucherSaved()
    {
        await LoadAsync();
    }

    private void AddVoucher_Click(object sender, RoutedEventArgs e)
    {
        if (_child == null)
        {
            MessageBox.Show("Please save the child record first before adding vouchers.",
                "Save Child First", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        VoucherList.Children.Insert(0, new VoucherRowControl(null, OnVoucherSaved, _child.ChildId));
    }

    // ── EDIT CHILD ────────────────────────────────────────────────────────
    private async void EditChild_Click(object sender, RoutedEventArgs e)
    {
        if (_child != null)
        {
            var lockedBy = await _lockService.TryAcquireAsync("Children", _child.ChildId);
            if (lockedBy != null)
            {
                MessageBox.Show($"This record is currently being edited by {lockedBy}.",
                    "Record Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        EnterEditMode();
    }

    private void EnterEditMode()
    {
        _isEditingChild = true;
        TxtFirstName.Text = _child?.FirstName ?? "";
        TxtLastName.Text = _child?.LastName ?? "";
        TxtDOB.Text = _child?.DateOfBirth?.ToString("MM/dd/yyyy") ?? "";
        TxtNotes.Text = _child?.Notes ?? "";

        // Toggle labels ↔ inputs
        foreach (var (lbl, txt) in new[] {
            (LblFirstName, TxtFirstName), (LblLastName, TxtLastName),
            (LblDOB, TxtDOB), (LblNotes, TxtNotes) })
        {
            lbl.Visibility = Visibility.Collapsed;
            txt.Visibility = Visibility.Visible;
        }

        AddAliasPanel.Visibility = Visibility.Visible;
        BtnEditChild.Visibility = Visibility.Collapsed;
        BtnSaveChild.Visibility = Visibility.Visible;
        BtnCancelChild.Visibility = Visibility.Visible;
    }

    private async void SaveChild_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtFirstName.Text) ||
            string.IsNullOrWhiteSpace(TxtLastName.Text))
        {
            MessageBox.Show("First and last name are required.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_child == null)
        {
            // Create new child
            _child = new Child
            {
                LocationId = App.CurrentLocation!.LocationId,
                CreatedByUserId = App.CurrentUser!.UserId
            };
            App.Db!.Children.Add(_child);
        }

        _child.FirstName = TxtFirstName.Text.Trim();
        _child.LastName = TxtLastName.Text.Trim();
        _child.Notes = TxtNotes.Text.Trim();
        _child.LastModifiedAt = DateTime.UtcNow;
        _child.LastModifiedByUserId = App.CurrentUser!.UserId;

        if (DateTime.TryParse(TxtDOB.Text, out var dob))
            _child.DateOfBirth = dob;

        await App.Db!.SaveChangesAsync();
        _childId = _child.ChildId;

        if (_child.ChildId > 0)
            await _lockService.ReleaseAsync("Children", _child.ChildId);

        // Refresh the child list in the main window so new children appear
        if (_mainWindow != null)
        {
            await _mainWindow.LoadChildrenAsync();
            _mainWindow.OpenChildProfile(_child.ChildId);
        }

        ExitEditMode();
        await LoadAsync();
    }

    private async void CancelChild_Click(object sender, RoutedEventArgs e)
    {
        if (_child != null)
            await _lockService.ReleaseAsync("Children", _child.ChildId);
        ExitEditMode();
        if (_childId != null) await LoadAsync();
    }

    private void ExitEditMode()
    {
        _isEditingChild = false;
        foreach (var (lbl, txt) in new[] {
            (LblFirstName, TxtFirstName), (LblLastName, TxtLastName),
            (LblDOB, TxtDOB), (LblNotes, TxtNotes) })
        {
            lbl.Visibility = Visibility.Visible;
            txt.Visibility = Visibility.Collapsed;
        }
        AddAliasPanel.Visibility = Visibility.Collapsed;
        BtnEditChild.Visibility = Visibility.Visible;
        BtnSaveChild.Visibility = Visibility.Collapsed;
        BtnCancelChild.Visibility = Visibility.Collapsed;
    }

    // ── ALIASES ───────────────────────────────────────────────────────────
    private async void AddAlias_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewAlias.Text.Trim();
        if (string.IsNullOrEmpty(name) || _child == null) return;

        if (_child.Aliases.Any(a => a.AliasName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("That alias already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        App.Db!.ChildAliases.Add(new ChildAlias
        {
            ChildId = _child.ChildId,
            AliasName = name,
            CreatedByUserId = App.CurrentUser!.UserId
        });
        await App.Db.SaveChangesAsync();
        TxtNewAlias.Text = "";
        await LoadAsync();
    }

    private async void RemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int aliasId)
        {
            var alias = await App.Db!.ChildAliases.FindAsync(aliasId);
            if (alias != null)
            {
                App.Db.ChildAliases.Remove(alias);
                await App.Db.SaveChangesAsync();
                await LoadAsync();
            }
        }
    }

    private void AddAdjustment_Click(object sender, RoutedEventArgs e)
    {
        if (_child == null) return;
        var dlg = new AddAdjustmentDialog(_child.ChildId,
            _child.Invoices.ToList());
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) _ = LoadAsync();
    }

    private void BtnArchive_Click(object sender, RoutedEventArgs e) => ArchiveChild();
    private void BtnUnarchive_Click(object sender, RoutedEventArgs e) => UnarchiveChild();

    public async void ArchiveChild()
    {
        if (_child == null) return;
        var activeVoucher = _child.Vouchers.FirstOrDefault(v => v.IsActive);
        var dlg = new ArchiveChildDialog(_child, activeVoucher);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true) return;

        _child.ArchivedAt = DateTime.UtcNow;
        _child.LastModifiedAt = DateTime.UtcNow;
        _child.LastModifiedByUserId = App.CurrentUser!.UserId;

        if (dlg.TerminateVoucher && activeVoucher != null && dlg.TerminationDate.HasValue)
        {
            activeVoucher.TerminationDate = dlg.TerminationDate.Value;
            activeVoucher.LastModifiedAt = DateTime.UtcNow;
            activeVoucher.LastModifiedByUserId = App.CurrentUser.UserId;
        }

        await App.Db!.SaveChangesAsync();
        if (_mainWindow != null) await _mainWindow.LoadChildrenAsync();
        await LoadAsync();
    }

    public async void UnarchiveChild()
    {
        if (_child == null) return;
        var result = MessageBox.Show(
            $"Unarchive {_child.FullName} and restore them to the active list?",
            "Confirm Unarchive", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _child.ArchivedAt = null;
        _child.LastModifiedAt = DateTime.UtcNow;
        _child.LastModifiedByUserId = App.CurrentUser!.UserId;
        await App.Db!.SaveChangesAsync();
        if (_mainWindow != null) await _mainWindow.LoadChildrenAsync();
        await LoadAsync();
    }

    // ── VIEW MODEL HELPERS ────────────────────────────────────────────────
    private record AdjRowVm(ManualAdjustment Adjustment, decimal RunningCreditAfter)
    {
        public DateTime AdjustmentDate => Adjustment.AdjustmentDate;
        public string ApplyTo => Adjustment.ApplyTo;
        public Invoice? Invoice => Adjustment.Invoice;
        public decimal Amount => Adjustment.Amount;
        public string? Reason => Adjustment.Reason;
    }
}
