using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public static class MetaDataTimelineBrushes
    {
        public static Brush Splash { get; } = CreateSplashBrush();

        private static Brush CreateSplashBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(220, 141, 85));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class MetaDataTimelineTypeBrushConverter :
        IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            var typeName = values.ElementAtOrDefault(0) as string ??
                string.Empty;
            var hash = GetStableHash(typeName);
            var typeColor = Color.FromRgb(
                (byte)(hash >> 8),
                (byte)(hash >> 24),
                (byte)(hash >> 40));
            var textColor = (values.ElementAtOrDefault(1) as
                SolidColorBrush)?.Color ?? Colors.White;
            var brush = new SolidColorBrush(Blend(
                typeColor,
                textColor,
                0.45));
            brush.Freeze();
            return brush;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture) => targetTypes
                .Select(_ => Binding.DoNothing)
                .ToArray();

        private static ulong GetStableHash(string value)
        {
            const ulong offsetBasis = 14695981039346656037;
            const ulong prime = 1099511628211;
            var hash = offsetBasis;
            foreach (var character in value)
            {
                hash ^= char.ToUpperInvariant(character);
                hash *= prime;
            }

            return hash;
        }

        private static Color Blend(
            Color color,
            Color semanticAnchor,
            double anchorWeight)
        {
            var colorWeight = 1 - anchorWeight;
            return Color.FromRgb(
                (byte)Math.Round(
                    color.R * colorWeight + semanticAnchor.R * anchorWeight),
                (byte)Math.Round(
                    color.G * colorWeight + semanticAnchor.G * anchorWeight),
                (byte)Math.Round(
                    color.B * colorWeight + semanticAnchor.B * anchorWeight));
        }
    }
}
