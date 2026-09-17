using System.Globalization;
using System.Windows.Data;

namespace PhoneAccounting.App.Infrastructure;

public class ActiveStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "نشط" : "موقوف";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
