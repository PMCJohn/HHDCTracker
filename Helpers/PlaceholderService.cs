using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HHDCTracker.Helpers;

/// <summary>
/// Attached property that adds placeholder text to any WPF TextBox.
/// Usage: helpers:PlaceholderService.Placeholder="Type here..."
/// </summary>
public static class PlaceholderService
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(PlaceholderService),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static string GetPlaceholder(DependencyObject d)
        => (string)d.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject d, string value)
        => d.SetValue(PlaceholderProperty, value);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        tb.Loaded -= Tb_Loaded;
        tb.Loaded += Tb_Loaded;
        if (tb.IsLoaded) SetupPlaceholder(tb);
    }

    private static void Tb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) SetupPlaceholder(tb);
    }

    private static void SetupPlaceholder(TextBox tb)
    {
        var text = GetPlaceholder(tb);
        if (string.IsNullOrEmpty(text)) return;

        // Use adorner layer approach via a transparent overlay TextBlock
        tb.TextChanged -= Tb_TextChanged;
        tb.TextChanged += Tb_TextChanged;
        UpdatePlaceholderVisibility(tb);
    }

    private static void Tb_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) UpdatePlaceholderVisibility(tb);
    }

    private static void UpdatePlaceholderVisibility(TextBox tb)
    {
        // Find the placeholder TextBlock tagged to this TextBox
        var parent = VisualTreeHelper.GetParent(tb);
        // We use Tag-based approach: set Tag on TextBox to reference its placeholder label
        if (tb.Tag is TextBlock placeholder)
            placeholder.Visibility = string.IsNullOrEmpty(tb.Text)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Creates a Grid wrapper containing a TextBox with an overlaid placeholder label.
    /// Call this in code-behind when building dynamic controls.
    /// For XAML, use the PlaceholderTextBox style in App.xaml instead.
    /// </summary>
    public static Grid Wrap(TextBox tb, string placeholder)
    {
        var grid = new Grid();
        var lbl = new TextBlock
        {
            Text = placeholder,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 120, 120, 120)),
            FontFamily = tb.FontFamily,
            FontSize = tb.FontSize,
            IsHitTestVisible = false,
            Margin = new Thickness(tb.Padding.Left + 2, tb.Padding.Top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        tb.Tag = lbl;
        grid.Children.Add(tb);
        grid.Children.Add(lbl);
        UpdatePlaceholderVisibility(tb);
        return grid;
    }
}
