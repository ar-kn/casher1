using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يُظهر العنصر عندما يكون النص غير فارغ، ويُخفيه عندما يكون فارغاً أو null.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
