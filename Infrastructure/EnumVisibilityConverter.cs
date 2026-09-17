using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>
/// يحوّل قيمة Enum إلى Visibility.Visible عندما تساوي اسم المعامل الممرر (ConverterParameter)،
/// وإلا Visibility.Collapsed. يُستخدم للتبديل بين أقسام واجهة نقطة البيع حسب وضع الفاتورة (Mode).
/// </summary>
public class EnumVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
