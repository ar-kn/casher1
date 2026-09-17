using System.Globalization;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يستخرج أول حرف أو حرفين من اسم لعرضهما داخل دائرة صورة رمزية (Avatar).</summary>
public class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrWhiteSpace(name)) return "";

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0][..1];
        return $"{parts[0][..1]}{parts[1][..1]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
