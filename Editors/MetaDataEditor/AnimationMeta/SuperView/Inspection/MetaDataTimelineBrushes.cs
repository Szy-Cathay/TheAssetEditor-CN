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
}
