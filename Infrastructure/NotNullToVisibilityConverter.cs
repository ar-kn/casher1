using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يُظهر العنصر عندما تكون القيمة غير null، ويُخفيه عندما تكون null (عكس NullToVisibilityConverter).</summary>
public class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
