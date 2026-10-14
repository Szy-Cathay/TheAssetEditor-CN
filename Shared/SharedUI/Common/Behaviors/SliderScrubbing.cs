using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Shared.Ui.Common.Behaviors;

/// <summary>Allows a track click to continue as a drag without changing keyboard seeking.</summary>
public static class SliderScrubbing
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SliderScrubbing), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty IsScrubbingProperty = DependencyProperty.RegisterAttached(
        "IsScrubbing", typeof(bool), typeof(SliderScrubbing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly DependencyProperty GrabOffsetProperty = DependencyProperty.RegisterAttached(
        "GrabOffset", typeof(double), typeof(SliderScrubbing), new PropertyMetadata(0d));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsScrubbing(DependencyObject element) => (bool)element.GetValue(IsScrubbingProperty);
    public static void SetIsScrubbing(DependencyObject element, bool value) => element.SetValue(IsScrubbingProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Slider slider)
            return;
        if ((bool)args.NewValue)
        {
            slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
            slider.PreviewMouseMove += OnMouseMove;
            slider.PreviewMouseLeftButtonUp += OnMouseUp;
            slider.LostMouseCapture += OnLostCapture;
            slider.Unloaded += OnUnloaded;
            slider.IsEnabledChanged += OnSliderEnabledChanged;
        }
        else
        {
            EndScrub(slider);
            slider.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseDown));
            slider.PreviewMouseMove -= OnMouseMove;
            slider.PreviewMouseLeftButtonUp -= OnMouseUp;
            slider.LostMouseCapture -= OnLostCapture;
            slider.Unloaded -= OnUnloaded;
            slider.IsEnabledChanged -= OnSliderEnabledChanged;
        }
    }

    private static Track? GetTrack(Slider slider) => slider.Template?.FindName("PART_Track", slider) as Track;

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var slider = (Slider)sender;
        var track = GetTrack(slider);
        if (!slider.IsEnabled || track == null || slider.Orientation != Orientation.Horizontal)
            return;

        var point = args.GetPosition(track);
        var thumb = track.Thumb;
        var thumbPoint = args.GetPosition(thumb);
        var grabbedThumb = new Rect(thumb.RenderSize).Contains(thumbPoint);
        slider.SetValue(GrabOffsetProperty, grabbedThumb
            ? point.X - thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, 0), track).X
            : 0d);
        if (!slider.CaptureMouse())
            return;

        slider.SetCurrentValue(IsScrubbingProperty, true);
        Seek(slider, point);
        args.Handled = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        var slider = (Slider)sender;
        if (!GetIsScrubbing(slider))
            return;
        if (args.LeftButton != MouseButtonState.Pressed || !slider.IsMouseCaptured)
        {
            EndScrub(slider);
            return;
        }
        if (GetTrack(slider) is Track track)
            Seek(slider, args.GetPosition(track));
        args.Handled = true;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs args)
    {
        var slider = (Slider)sender;
        if (!GetIsScrubbing(slider))
            return;
        if (GetTrack(slider) is Track track)
            Seek(slider, args.GetPosition(track));
        EndScrub(slider);
    }

    private static void Seek(Slider slider, Point position)
    {
        if (GetTrack(slider) is not Track track)
            return;
        position.X -= (double)slider.GetValue(GrabOffsetProperty);
        var value = track.ValueFromPoint(position);
        if (double.IsFinite(value))
            slider.SetCurrentValue(RangeBase.ValueProperty, Math.Clamp(value, slider.Minimum, slider.Maximum));
    }

    private static void EndScrub(Slider slider)
    {
        slider.SetCurrentValue(IsScrubbingProperty, false);
        if (slider.IsMouseCaptured)
            slider.ReleaseMouseCapture();
    }

    private static void OnLostCapture(object sender, MouseEventArgs args) => EndScrub((Slider)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs args) => EndScrub((Slider)sender);
    private static void OnSliderEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue)
            EndScrub((Slider)sender);
    }
}
