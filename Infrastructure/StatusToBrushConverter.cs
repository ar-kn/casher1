using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يحوّل نص حالة المخزون ("متوفر"/"منخفض"/"نفد") إلى لون مناسب (أخضر/برتقالي/أحمر).</summary>
public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Available = MakeBrush("#16A34A");
    private static readonly SolidColorBrush Low = MakeBrush("#D97706");
    private static readonly SolidColorBrush Out = MakeBrush("#BA1A1A");

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "منخفض" => Low,
            "نفد" => Out,
            _ => Available
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>يحوّل نص حالة المخزون إلى خلفية فاتحة (Tint) مناسبة لشارة الحالة.</summary>
public class StatusToTintBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Available = MakeBrush("#E3FBE8");
    private static readonly SolidColorBrush Low = MakeBrush("#FEF3C7");
    private static readonly SolidColorBrush Out = MakeBrush("#FFDAD6");

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "منخفض" => Low,
            "نفد" => Out,
            _ => Available
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
