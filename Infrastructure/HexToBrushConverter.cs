using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يحوّل نص لون بصيغة Hex (مثل "#0071E3") إلى SolidColorBrush جاهزة للعرض.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
