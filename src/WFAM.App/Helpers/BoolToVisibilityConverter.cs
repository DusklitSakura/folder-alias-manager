using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WFAM.App.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        if (Inverse) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
