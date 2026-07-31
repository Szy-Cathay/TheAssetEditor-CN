using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Editors.CscEditor.Views
{
    public sealed class CscFloatConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture) =>
            value;

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not string text)
                return DependencyProperty.UnsetValue;

            var normalized = text
                .Replace(',', '.')
                .Replace('\uFF0E', '.')
                .Replace('\u3002', '.');

            return float.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var result) &&
                   float.IsFinite(result) &&
                   (!string.Equals(
                        parameter as string,
                        "NonNegative",
                        StringComparison.Ordinal) ||
                    result >= 0)
                ? result
                : DependencyProperty.UnsetValue;
        }
    }
}
