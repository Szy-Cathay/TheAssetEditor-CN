using System.Windows;
using System.Windows.Controls;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public sealed class MetaDataTimelinePanel : Panel
    {
        private const double MinimumHitWidth = 12;
        private const double MinimumHeight = 18;
        private const double LaneHeight = 10;
        private const double LaneGap = 2;
        private const double VerticalPadding = 1;

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsFinite(availableSize.Width)
                ? availableSize.Width
                : 0;
            var layout = CreateLayout(width);
            foreach (var item in layout.Items)
                item.Child.Measure(new Size(item.Width, LaneHeight));

            var height = Math.Max(
                MinimumHeight,
                layout.LaneCount * LaneHeight + VerticalPadding * 2);
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var layout = CreateLayout(finalSize.Width);
            var top = Math.Max(
                VerticalPadding,
                (finalSize.Height - layout.LaneCount * LaneHeight) / 2);
            foreach (var item in layout.Items)
            {
                var markerLayer = item.Marker.Item.TimelineMarkerKind switch
                {
                    MetaDataTimelineMarkerKind.Instant => 20,
                    MetaDataTimelineMarkerKind.Range => 10,
                    _ => 0,
                };
                SetZIndex(
                    item.Child,
                    markerLayer + (item.Marker.IsSelected ? 1 : 0));
                item.Child.Arrange(new Rect(
                    item.Left,
                    top + item.Lane * LaneHeight,
                    item.Width,
                    LaneHeight));
            }

            return finalSize;
        }

        private TimelineLayout CreateLayout(double width)
        {
            var items = InternalChildren
                .OfType<FrameworkElement>()
                .Select((child, index) => CreateLayoutItem(
                    child,
                    index,
                    width))
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .OrderBy(item => item.Left)
                .ThenBy(item => item.Right)
                .ThenBy(item => item.Index)
                .ToArray();
            var laneEnds = new List<double>();
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var item = items[itemIndex];
                var lane = laneEnds.FindIndex(end =>
                    end + LaneGap <= item.Left);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(item.Right);
                }
                else
                {
                    laneEnds[lane] = item.Right;
                }

                items[itemIndex] = item with { Lane = lane };
            }

            return new TimelineLayout(items, Math.Max(1, laneEnds.Count));
        }

        private static TimelineLayoutItem? CreateLayoutItem(
            FrameworkElement child,
            int index,
            double availableWidth)
        {
            if (child.DataContext is not MetaDataTimelineMarkerViewModel marker)
                return null;

            var isInstant = marker.Item.TimelineMarkerKind ==
                MetaDataTimelineMarkerKind.Instant;
            var left = marker.StartFraction * availableWidth;
            var width = isInstant
                ? MinimumHitWidth
                : Math.Max(
                    MinimumHitWidth,
                    (marker.EndFraction - marker.StartFraction) *
                        availableWidth);
            if (isInstant)
                left -= width / 2;
            left = Math.Clamp(
                left,
                0,
                Math.Max(0, availableWidth - width));
            width = Math.Min(width, availableWidth);
            return new TimelineLayoutItem(
                child,
                marker,
                index,
                left,
                width,
                0);
        }

        private readonly record struct TimelineLayout(
            IReadOnlyList<TimelineLayoutItem> Items,
            int LaneCount);

        private readonly record struct TimelineLayoutItem(
            FrameworkElement Child,
            MetaDataTimelineMarkerViewModel Marker,
            int Index,
            double Left,
            double Width,
            int Lane)
        {
            public double Right => Left + Width;
        }
    }
}
