using System.Globalization;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>
/// يقارن قيمة عدد صحيح (int) بالمعامل الممرر (ConverterParameter) ويعيد true عند التطابق.
/// يُستخدم لربط أزرار تحكم مجزّأة (Segmented Control) بخاصية فهرس (Index) عددية.
/// </summary>
public class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int intValue || parameter is null) return false;
        return int.TryParse(parameter.ToString(), out var target) && intValue == target;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null &&
            int.TryParse(parameter.ToString(), out var target))
        {
            return target;
        }
        return Binding.DoNothing;
    }
}
