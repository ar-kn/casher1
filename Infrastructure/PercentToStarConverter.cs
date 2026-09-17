using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>
/// يحوّل نسبة مئوية (0-100) إلى GridLength بوحدة Star، لبناء شريط تقدم عبر عمودي Grid
/// (عمود مملوء + عمود فارغ) دون الحاجة لمعرفة العرض الفعلي بالبكسل.
/// مرّر ConverterParameter="Inverse" للحصول على الجزء المتبقي (100-النسبة).
/// </summary>
public class PercentToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            decimal m => (double)m,
            int i => i,
            _ => 0.0
        };
        percent = Math.Clamp(percent, 0, 100);

        var isInverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        var star = isInverse ? 100 - percent : percent;

        // GridLength لا تقبل صفراً كوزن Star صراحة بشكل موثوق دائماً، فنستخدم حداً أدنى بسيطاً
        return new GridLength(Math.Max(0.001, star), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
