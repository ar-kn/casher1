using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يُظهر العنصر عندما تكون القيمة null، ويُخفيه عندما تكون موجودة.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
