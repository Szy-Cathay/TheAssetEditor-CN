using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace KitbasherEditor.Views;

public sealed class ViewportToolbarBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 3 || values[1] is not SolidColorBrush foreground || values[2] is not SolidColorBrush canvas)
            return DependencyProperty.UnsetValue;
        if (values[0] is not Color background)
            return foreground;
        return Contrast(background, foreground.Color) >= Contrast(background, canvas.Color) ? foreground : canvas;
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
