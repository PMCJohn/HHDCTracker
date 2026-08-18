using HHDCTracker.Models;
using HHDCTracker.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HHDCTracker.Views.Children;

public partial class VoucherRowControl : UserControl
{
    private Voucher? _voucher;
    private int? _childId;
    private bool _isExpanded = false;
    private bool _isEditing = false;
    private readonly Action _onSaved;
    private readonly LockService _lockService;

    public VoucherRowControl(Voucher? voucher, Action onSaved, int? childId = null)
    {
        InitializeComponent();
        _voucher = voucher;
        _onSaved = onSaved;
        _childId = childId ?? voucher?.ChildId;
        _lockService = new LockService(App.Db!, App.CurrentUser!.UserId);

        if (voucher == null)
        {
            // New voucher — expand and go straight to edit
            DetailPanel.Visibility = Visibility.Visible;
            ExpandArrow.Text = "▼";
            _isExpanded = true;
            EnterEditMode();
        }
        else
        {
            PopulateSummaryRow();
        }
    }

    // ── SUMMARY ROW ───────────────────────────────────────────────────────
    private void PopulateSummaryRow()
    {
        if (_voucher == null) return;

        LblVoucherNum.Text = _voucher.VoucherNumber;
        LblRateType.Text = _voucher.RateType;
        LblPeriod.Text = $"{_voucher.PeriodStart:MM/dd/yyyy} → " +
                         (_voucher.PeriodEnd.HasValue
                             ? _voucher.PeriodEnd.Value.ToString("MM/dd/yyyy")
                             : "ongoing");
        LblVO.Text = $"${_voucher.VOPromisedWeekly:N2}";
        LblHHDC.Text = $"${_voucher.HHDCChargeWeekly:N2}";

        var copay = _voucher.ExpectedWeeklyCopay;
        LblCopay.Text = $"${copay:N2}";
        LblCopay.Foreground = copay < 0
            ? new SolidColorBrush(Color.FromRgb(39, 174, 96))
            : new SolidColorBrush(Color.FromRgb(26, 82, 118));

        LblSummer.Text = _voucher.SummerRateStart.HasValue
            ? $"{_voucher.SummerRateStart:MM/dd} – {_voucher.SummerRateEnd:MM/dd} " +
              $"(VO: ${_voucher.VOSummerWeekly:N2} / HHDC: ${_voucher.HHDCSummerWeekly:N2})"
            : "—";

        // Status badge
        if (_voucher.TerminationDate.HasValue)
        {
            LblStatus.Text = "Terminated";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(250, 219, 216));
        }
        else if (_voucher.PeriodEnd.HasValue && _voucher.PeriodEnd < DateTime.Today)
        {
            LblStatus.Text = "Expired";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(253, 235, 208));
        }
        else if (_voucher.PeriodStart > DateTime.Today)
        {
            LblStatus.Text = "Pending";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(254, 249, 231));
        }
        else
        {
            LblStatus.Text = "Active";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(213, 245, 227));
        }

        PopulateDetailLabels();
    }

    private void PopulateDetailLabels()
    {
        if (_voucher == null) return;
        DLblVoucherNum.Text = _voucher.VoucherNumber;
        DLblRateType.Text = _voucher.RateType;
        DLblStart.Text = _voucher.PeriodStart.ToString("MM/dd/yyyy");
        DLblEnd.Text = _voucher.PeriodEnd?.ToString("MM/dd/yyyy") ?? "—";
        DLblTerm.Text = _voucher.TerminationDate?.ToString("MM/dd/yyyy") ?? "—";
        DLblVO.Text = $"${_voucher.VOPromisedWeekly:N2}";
        DLblHHDC.Text = $"${_voucher.HHDCChargeWeekly:N2}";
        DLblSumStart.Text = _voucher.SummerRateStart?.ToString("MM/dd/yyyy") ?? "—";
        DLblSumEnd.Text = _voucher.SummerRateEnd?.ToString("MM/dd/yyyy") ?? "—";
        DLblSumVO.Text = _voucher.VOSummerWeekly.HasValue
            ? $"${_voucher.VOSummerWeekly:N2}" : "—";
        DLblSumHHDC.Text = _voucher.HHDCSummerWeekly.HasValue
            ? $"${_voucher.HHDCSummerWeekly:N2}" : "—";
        DLblPCA.Text = _voucher.PCACodeLabel ?? "—";
    }

    // ── EXPAND / COLLAPSE ─────────────────────────────────────────────────
    private void Header_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isExpanded = !_isExpanded;
        DetailPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandArrow.Text = _isExpanded ? "▼" : "▶";
    }

    // ── EDIT ──────────────────────────────────────────────────────────────
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_voucher != null)
        {
            var lockedBy = await _lockService.TryAcquireAsync("Vouchers", _voucher.VoucherId);
            if (lockedBy != null)
            {
                MessageBox.Show($"This voucher is being edited by {lockedBy}.",
                    "Record Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        EnterEditMode();
    }

    private void EnterEditMode()
    {
        _isEditing = true;
        DTxtVoucherNum.Text = _voucher?.VoucherNumber ?? "";
        DTxtStart.Text = _voucher?.PeriodStart.ToString("MM/dd/yyyy") ?? "";
        DTxtEnd.Text = _voucher?.PeriodEnd?.ToString("MM/dd/yyyy") ?? "";
        DTxtTerm.Text = _voucher?.TerminationDate?.ToString("MM/dd/yyyy") ?? "";
        DTxtVO.Text = _voucher?.VOPromisedWeekly.ToString("N2") ?? "";
        DTxtHHDC.Text = _voucher?.HHDCChargeWeekly.ToString("N2") ?? "";
        DTxtSumStart.Text = _voucher?.SummerRateStart?.ToString("MM/dd/yyyy") ?? "";
        DTxtSumEnd.Text = _voucher?.SummerRateEnd?.ToString("MM/dd/yyyy") ?? "";
        DTxtSumVO.Text = _voucher?.VOSummerWeekly?.ToString("N2") ?? "";
        DTxtSumHHDC.Text = _voucher?.HHDCSummerWeekly?.ToString("N2") ?? "";
        DTxtPCA.Text = _voucher?.PCACodeLabel ?? "";

        // Set rate type combo
        DCboRateType.SelectedIndex = _voucher?.RateType switch
        {
            "School-Year Rate" => 1,
            "Summer Rate" => 2,
            _ => 0
        };

        ToggleEditVisibility(true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DTxtVoucherNum.Text))
        {
            MessageBox.Show("Voucher number is required.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!DateTime.TryParse(DTxtStart.Text, out var start) ||
            !decimal.TryParse(DTxtVO.Text, out var vo) ||
            !decimal.TryParse(DTxtHHDC.Text, out var hhdc))
        {
            MessageBox.Show("Please check Period Start, VO Rate, and HHDC Rate — " +
                "all are required and must be valid numbers.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_voucher == null)
        {
            _voucher = new Voucher
            {
                ChildId = _childId!.Value,
                CreatedByUserId = App.CurrentUser!.UserId
            };
            App.Db!.Vouchers.Add(_voucher);
        }

        _voucher.VoucherNumber = DTxtVoucherNum.Text.Trim();
        _voucher.RateType = (DCboRateType.SelectedItem as ComboBoxItem)?.Content?.ToString()
                            ?? "Normal Rate";
        _voucher.PeriodStart = start;
        _voucher.PeriodEnd = DateTime.TryParse(DTxtEnd.Text, out var end) ? end : null;
        _voucher.TerminationDate = DateTime.TryParse(DTxtTerm.Text, out var term) ? term : null;
        _voucher.VOPromisedWeekly = vo;
        _voucher.HHDCChargeWeekly = hhdc;
        _voucher.PCACodeLabel = DTxtPCA.Text.Trim();
        _voucher.LastModifiedAt = DateTime.UtcNow;
        _voucher.LastModifiedByUserId = App.CurrentUser!.UserId;

        // Summer rate — only save if start and end are provided
        if (DateTime.TryParse(DTxtSumStart.Text, out var sumStart) &&
            DateTime.TryParse(DTxtSumEnd.Text, out var sumEnd))
        {
            _voucher.SummerRateStart = sumStart;
            _voucher.SummerRateEnd = sumEnd;
            _voucher.VOSummerWeekly = decimal.TryParse(DTxtSumVO.Text, out var svo) ? svo : null;
            _voucher.HHDCSummerWeekly = decimal.TryParse(DTxtSumHHDC.Text, out var shhdc) ? shhdc : null;
        }
        else
        {
            _voucher.SummerRateStart = null;
            _voucher.SummerRateEnd = null;
            _voucher.VOSummerWeekly = null;
            _voucher.HHDCSummerWeekly = null;
        }

        await App.Db!.SaveChangesAsync();
        if (_voucher.VoucherId > 0)
            await _lockService.ReleaseAsync("Vouchers", _voucher.VoucherId);

        _onSaved.Invoke();
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_voucher != null)
            await _lockService.ReleaseAsync("Vouchers", _voucher.VoucherId);

        if (_voucher == null)
        {
            // Was a new voucher — remove this control from parent
            if (Parent is Panel panel) panel.Children.Remove(this);
            return;
        }

        ToggleEditVisibility(false);
        PopulateDetailLabels();
    }

    private void ToggleEditVisibility(bool editing)
    {
        var lbls = new[] { DLblVoucherNum, DLblRateType, DLblStart, DLblEnd, DLblTerm,
                           DLblVO, DLblHHDC, DLblSumStart, DLblSumEnd, DLblSumVO,
                           DLblSumHHDC, DLblPCA };
        var txts = new[] { DTxtVoucherNum, DTxtStart, DTxtEnd, DTxtTerm,
                           DTxtVO, DTxtHHDC, DTxtSumStart, DTxtSumEnd,
                           DTxtSumVO, DTxtSumHHDC, DTxtPCA };

        foreach (var l in lbls)
            l.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        foreach (var t in txts)
            t.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        DCboRateType.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        DLblRateType.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

        BtnEdit.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        BtnSave.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        BtnCancel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
    }
}
