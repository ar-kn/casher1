using System.Globalization;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يقارن نصاً بالمعامل الممرر (ConverterParameter) ويعيد true عند التطابق الحرفي.</summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || parameter is null) return false;
        return string.Equals(s, parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null)
            return parameter.ToString()!;
        return Binding.DoNothing;
    }
}

/// <summary>يعيد true إذا كانت القيمة العددية (decimal/int/double) أكبر من صفر.</summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            decimal m => m > 0,
            int i => i > 0,
            double d => d > 0,
            _ => false
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
