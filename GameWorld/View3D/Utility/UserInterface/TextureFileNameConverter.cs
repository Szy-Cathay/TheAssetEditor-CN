using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace GameWorld.Core.Utility.UserInterface
{
    public sealed class TextureFileNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is not [string name, double width, TextBlock label])
                return "";
            if (width <= 0)
                return name;

            var typeface = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
            var pixelsPerDip = VisualTreeHelper.GetDpi(label).PixelsPerDip;
            double Measure(string text) => new FormattedText(text, culture, FlowDirection.LeftToRight,
                typeface, label.FontSize, Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;
            if (Measure(name) <= width)
                return name;

            // Keep the texture role and extension visible when model names share a long prefix.
            var characterStarts = StringInfo.ParseCombiningCharacters(name);
            string Tail(int length) => length == 0 ? "" : name[characterStarts[characterStarts.Length - length]..];
            var low = 0;
            var high = characterStarts.Length;
            while (low < high)
            {
                var length = (low + high + 1) / 2;
                if (Measure("…" + Tail(length)) <= width)
                    low = length;
                else
                    high = length - 1;
            }
            return "…" + Tail(low);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
