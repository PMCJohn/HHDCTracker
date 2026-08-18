using System.Globalization;
using System.Windows.Data;

namespace HHDCTracker.Helpers;

/// <summary>
/// Returns true if a decimal/double value is negative — used for row highlighting.
/// </summary>
public class NegativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            decimal d => d < 0,
            double db => db < 0,
            float f => f < 0,
            _ => false
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
