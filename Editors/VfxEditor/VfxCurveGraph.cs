using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shared.Core.Services;

namespace Editors.VfxEditor;

// This editor-local graph shows key positions. It does not simulate particle rendering.
public sealed class VfxCurveGraph : Control
{
    private VfxCurveViewModel? _subscribed;
    private Point? _preview;
    private bool _dragging;
    private (double X0, double X1, double Y0, double Y1) _range;
    private Rect Plot => new(64, 20, Math.Max(1, ActualWidth - 88), Math.Max(1, ActualHeight - 60));

    public VfxCurveGraph()
    {
        Focusable = true;
        SetResourceReference(BackgroundProperty, "AeBrush.Surface1");
        SetResourceReference(ForegroundProperty, "AeBrush.TextMuted");
        SetResourceReference(FocusVisualStyleProperty, "AeFocus.Keyboard");
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (_subscribed != null) _subscribed.PropertyChanged -= Changed; _subscribed = null; };
    }

    private void Subscribe()
    {
        if (_subscribed != null) _subscribed.PropertyChanged -= Changed;
        _subscribed = DataContext as VfxCurveViewModel;
        if (_subscribed != null) _subscribed.PropertyChanged += Changed;
        CancelDrag();
        InvalidateVisual();
    }

    private void Changed(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private Brush Brush(string key) => (Brush)FindResource("AeBrush." + key);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Background, new Pen(Brush("Border"), 1), new Rect(RenderSize));
        var channel = _subscribed?.SelectedChannel;
        if (channel == null || channel.Points.Count == 0) return;
        var points = channel.Points;
        if (!_dragging)
        {
            var minY = Math.Min(0, points.Min(x => x.Model.Value));
            var maxY = Math.Max(0, points.Max(x => x.Model.Value));
            if (minY == maxY) maxY = minY + 1;
            var padding = (maxY - minY) * 0.15;
            _range = (Math.Min(0, points.Min(x => x.Model.Time)), Math.Max(1, points.Max(x => x.Model.Time)), minY - padding, maxY + padding);
        }
        var plot = Plot;
        for (var i = 0; i <= 4; i++)
        {
            var x = plot.Left + plot.Width * i / 4;
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(new Pen(Brush("Border"), 0.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawLine(new Pen(Brush("Border"), 0.5), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, (_range.X0 + (_range.X1 - _range.X0) * i / 4).ToString("G3", CultureInfo.InvariantCulture), new Point(x - 12, plot.Bottom + 8));
            DrawText(dc, (_range.Y0 + (_range.Y1 - _range.Y0) * i / 4).ToString("G3", CultureInfo.InvariantCulture), new Point(4, y - 6));
        }
        var locations = points.Select(x => ToScreen(x == _subscribed!.SelectedPoint && _preview.HasValue
            ? _preview.Value : new Point(x.Model.Time, x.Model.Value))).ToArray();
        dc.PushClip(new RectangleGeometry(plot));
        var linePen = new Pen(Brush("Accent"), 2);
        // Nonlinear segments are deliberately shown as a dashed control polygon.
        for (var i = 1; i < locations.Length; i++)
        {
            var linear = points[i - 1].Model.OutMode == "linear" && points[i].Model.InMode == "linear";
            var pen = linear ? linePen : new Pen(Brush("Accent"), 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, locations[i - 1], locations[i]);
        }
        if (locations.Length == 1)
            dc.DrawLine(linePen, new Point(plot.Left, locations[0].Y), new Point(plot.Right, locations[0].Y));
        dc.Pop();
        for (var i = 0; i < locations.Length; i++)
        {
            var selected = points[i] == _subscribed!.SelectedPoint;
            dc.DrawEllipse(selected ? Brush("Accent") : Brush("Surface1"), new Pen(Brush("Accent"), 2), locations[i], selected ? 6 : 4, selected ? 6 : 4);
        }
    }

    private void DrawText(DrawingContext dc, string text, Point position)
    {
        var family = (FontFamily)FindResource("AppFontFamily");
        var size = (double)((Style)FindResource("AeText.Caption")).Setters.OfType<Setter>()
            .First(x => x.Property == TextBlock.FontSizeProperty).Value;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size,
            Brush("TextMuted"), VisualTreeHelper.GetDpi(this).PixelsPerDip), position);
    }

    private Point ToScreen(Point value) => new(Plot.Left + (value.X - _range.X0) / (_range.X1 - _range.X0) * Plot.Width,
        Plot.Bottom - (value.Y - _range.Y0) / (_range.Y1 - _range.Y0) * Plot.Height);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_subscribed?.HasErrors != false || _subscribed.SelectedChannel == null) return;
        var mouse = e.GetPosition(this);
        var hit = _subscribed.SelectedChannel.Points.OrderBy(x => (ToScreen(new Point(x.Model.Time, x.Model.Value)) - mouse).Length)
            .FirstOrDefault(x => (ToScreen(new Point(x.Model.Time, x.Model.Value)) - mouse).Length <= 12);
        if (hit == null) return;
        Focus();
        _subscribed.SelectedPoint = hit;
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _subscribed?.SelectedPoint == null) return;
        var mouse = e.GetPosition(this);
        var time = _range.X0 + Math.Clamp((mouse.X - Plot.Left) / Plot.Width, 0, 1) * (_range.X1 - _range.X0);
        var value = _range.Y0 + Math.Clamp((Plot.Bottom - mouse.Y) / Plot.Height, 0, 1) * (_range.Y1 - _range.Y0);
        var points = _subscribed.SelectedChannel!.Points;
        var index = _subscribed.SelectedPoint.Index;
        var minTime = index > 0 ? Math.BitIncrement(points[index - 1].Model.Time) : _range.X0;
        var maxTime = index + 1 < points.Count ? Math.BitDecrement(points[index + 1].Model.Time) : _range.X1;
        time = minTime <= maxTime ? Math.Clamp(time, minTime, maxTime) : _subscribed.SelectedPoint.Model.Time;
        _preview = new Point(time, value);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        var preview = _preview;
        CancelDrag();
        if (preview.HasValue) _subscribed?.MoveSelected(preview.Value.X, preview.Value.Y);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); CancelDrag(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CancelDrag(); e.Handled = true; }
    }

    private void CancelDrag()
    {
        _dragging = false;
        _preview = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
}
