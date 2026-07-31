using System.Threading;
using System.Windows;
using System.Windows.Input;
using Editors.CscEditor.Data;
using Editors.CscEditor.Views;
using Shared.GameFormats.Esf;

namespace Test.CscEditor;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class CscCurveHistoryTests
{
    [Test]
    public void Double_click_add_and_right_click_delete_are_each_one_gesture()
    {
        var control = CreateControl();
        var starts = 0;
        var completions = 0;
        control.EditGestureStarted += () => starts++;
        control.EditGestureCompleted += () => completions++;

        control.AddKeyAt(new Point(100, 80));
        var key = control.SelectedSeries!.Channel.Keyframes.Single();
        control.DeleteKey(control.SelectedSeries, key);

        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(completions, Is.EqualTo(2));
            Assert.That(
                control.SelectedSeries.Channel.Keyframes,
                Is.Empty);
        });
    }

    [Test]
    public void Key_drag_with_many_updates_is_one_gesture()
    {
        var control = CreateControl();
        var series = control.SelectedSeries!;
        var key = series.Channel.AddKeyframe(0, 1);
        var starts = 0;
        var completions = 0;
        control.EditGestureStarted += () => starts++;
        control.EditGestureCompleted += () => completions++;

        control.BeginKeyGesture(series, key);
        control.UpdateDraggedKey(1, 2);
        control.UpdateDraggedKey(2, 3);
        control.CompleteEditGesture();

        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(key.Time, Is.EqualTo(2));
            Assert.That(key.Value, Is.EqualTo(3));
        });
    }

    [Test]
    public void Handle_drag_marks_segment_bezier_inside_the_same_gesture()
    {
        var control = CreateControl();
        var series = control.SelectedSeries!;
        var first = series.Channel.AddKeyframe(0, 1);
        var second = series.Channel.AddKeyframe(2, 2);
        var starts = 0;
        var completions = 0;
        control.EditGestureStarted += () => starts++;
        control.EditGestureCompleted += () => completions++;

        control.BeginHandleGesture(
            series,
            first,
            isOut: true);
        control.UpdateSelectedHandle(
            new Coord2d(0.5f, 1));
        control.UpdateSelectedHandle(
            new Coord2d(0.75f, 2));
        control.CompleteEditGesture();

        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(first.ModeOut, Is.EqualTo("bezier_c"));
            Assert.That(second.ModeIn, Is.EqualTo("bezier_c"));
            Assert.That(
                first.TangentOut,
                Is.EqualTo(new Coord2d(0.75f, 2)));
        });
    }

    [Test]
    public void Lost_mouse_capture_ends_gesture_and_clears_drag_state()
    {
        var control = CreateControl();
        var series = control.SelectedSeries!;
        var key = series.Channel.AddKeyframe(0, 1);
        var completions = 0;
        control.EditGestureCompleted += () => completions++;
        control.BeginKeyGesture(series, key);
        control.UpdateDraggedKey(1, 2);

        control.RaiseEvent(new MouseEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount)
        {
            RoutedEvent = Mouse.LostMouseCaptureEvent,
        });
        control.UpdateDraggedKey(5, 9);

        Assert.Multiple(() =>
        {
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(key.Time, Is.EqualTo(1));
            Assert.That(key.Value, Is.EqualTo(2));
        });
    }

    private static CurveEditorControl CreateControl()
    {
        var channel = new CscChannel
        {
            Header = 0,
        };
        var series = new CurveSeries
        {
            Name = "X",
            Colour = System.Windows.Media.Colors.Red,
            Channel = channel,
        };
        var control = new CurveEditorControl
        {
            SeriesSource = [series],
            SelectedSeries = series,
            Duration = 10,
        };
        control.Measure(new Size(400, 200));
        control.Arrange(new Rect(0, 0, 400, 200));
        return control;
    }
}
