using System.Windows;
using System.Windows.Controls;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public sealed class MetaDataTimelinePanel : Panel
    {
        private const double MinimumHitWidth = 12;

        protected override Size MeasureOverride(Size availableSize)
        {
            var height = double.IsFinite(availableSize.Height)
                ? availableSize.Height
                : 18;
            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(double.PositiveInfinity, height));

            var width = double.IsFinite(availableSize.Width)
                ? availableSize.Width
                : 0;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (UIElement child in InternalChildren)
            {
                if (child is not FrameworkElement element ||
                    element.DataContext is not
                        MetaDataTimelineMarkerViewModel marker)
                {
                    child.Arrange(new Rect(finalSize));
                    continue;
                }

                var isInstant = marker.Item.TimelineMarkerKind ==
                    MetaDataTimelineMarkerKind.Instant;
                var left = marker.StartFraction * finalSize.Width;
                var width = isInstant
                    ? MinimumHitWidth
                    : Math.Max(
                        MinimumHitWidth,
                        (marker.EndFraction - marker.StartFraction) *
                            finalSize.Width);
                if (isInstant)
                    left -= width / 2;
                left = Math.Clamp(left, 0, Math.Max(0, finalSize.Width - width));
                width = Math.Min(width, finalSize.Width);
                SetZIndex(child, marker.IsSelected ? 1 : 0);
                child.Arrange(new Rect(left, 0, width, finalSize.Height));
            }

            return finalSize;
        }
    }
}
