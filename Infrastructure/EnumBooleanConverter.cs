using System.Globalization;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>
/// يربط قيمة Enum بخاصية IsChecked في ToggleButton/RadioButton بحيث يكون العنصر
/// محدداً فقط عندما تساوي قيمة المصدر اسم المعامل الممرر (ConverterParameter).
/// يُستخدم لتحويل قائمة طريقة الدفع إلى مجموعة أزرار مجزأة (Segmented Buttons).
/// </summary>
public class EnumBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null)
        {
            return Enum.Parse(targetType, parameter.ToString()!);
        }
        return Binding.DoNothing;
    }
}
