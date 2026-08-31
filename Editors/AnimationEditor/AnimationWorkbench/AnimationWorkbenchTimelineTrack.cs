using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class AnimationWorkbenchTimelineEditResultEventArgs : EventArgs
{
    public AnimationWorkbenchTimelineEditResultEventArgs(
        AnimationWorkbenchTimelineEditResult result)
    {
        Result = result;
    }

    public AnimationWorkbenchTimelineEditResult Result { get; }
}

public sealed class AnimationWorkbenchTimelineTrack : FrameworkElement
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchTimelineController),
            typeof(AnimationWorkbenchTimelineTrack),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnControllerChanged));

    public static readonly DependencyProperty BoneNameProperty =
        DependencyProperty.Register(
            nameof(BoneName),
            typeof(string),
            typeof(AnimationWorkbenchTimelineTrack),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsRulerProperty =
        DependencyProperty.Register(
            nameof(IsRuler),
            typeof(bool),
            typeof(AnimationWorkbenchTimelineTrack),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MinimumDragDistance = 4;
    private Point _dragStart;
    private DragMode _dragMode;
    private bool _extendBoxSelection;
    private bool _controllerSubscribed;

    public AnimationWorkbenchTimelineTrack()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) =>
        {
            SubscribeController();
            InvalidateVisual();
        };
        Unloaded += (_, _) => UnsubscribeController(Controller);
    }

    public event EventHandler<AnimationWorkbenchTimelineEditResultEventArgs>?
        EditResultAvailable;

    public AnimationWorkbenchTimelineController? Controller
    {
        get => (AnimationWorkbenchTimelineController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    public string BoneName
    {
        get => (string)GetValue(BoneNameProperty);
        set => SetValue(BoneNameProperty, value);
    }

    public bool IsRuler
    {
        get => (bool)GetValue(IsRulerProperty);
        set => SetValue(IsRulerProperty, value);
    }

    public int LastRenderedMarkerCount { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        LastRenderedMarkerCount = 0;
        var controller = Controller;
        if (controller == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var borderBrush = FindBrush(
            "AeBrush.Border",
            SystemColors.ControlDarkBrush);
        var mutedBrush = FindBrush(
            "AeBrush.TextMuted",
            SystemColors.GrayTextBrush);
        var markerBrush = FindBrush(
            "AeBrush.TextSecondary",
            SystemColors.ControlTextBrush);
        var selectedBrush = FindBrush(
            "AeBrush.Accent",
            SystemColors.HighlightBrush);
        var selectionBrush = FindBrush(
            "AeBrush.AccentSoft",
            SystemColors.HighlightBrush);

        drawingContext.DrawLine(
            new Pen(borderBrush, 1),
            new Point(0, ActualHeight - 0.5),
            new Point(ActualWidth, ActualHeight - 0.5));

        DrawSelectionRange(drawingContext, controller, selectionBrush);
        if (IsRuler)
            DrawRuler(drawingContext, controller, borderBrush, mutedBrush);

        foreach (var frameIndex in controller.VisibleFrameIndices)
        {
            var x = FrameCenterX(controller, frameIndex);
            if (x < -4 || x > ActualWidth + 4)
                continue;
            var isSelected = controller.IsFrameSelected(frameIndex);
            var radius = isSelected ? 4 : 3;
            drawingContext.DrawEllipse(
                isSelected ? selectedBrush : markerBrush,
                null,
                new Point(x, IsRuler ? ActualHeight - 7 : ActualHeight / 2),
                radius,
                radius);
            LastRenderedMarkerCount++;
        }

        if (controller.FocusedFrameIndex >= 0)
        {
            var focusedX = FrameCenterX(
                controller,
                controller.FocusedFrameIndex);
            drawingContext.DrawLine(
                new Pen(selectedBrush, 1),
                new Point(focusedX, 0),
                new Point(focusedX, ActualHeight));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var controller = Controller;
        if (controller == null || controller.Timeline.FrameCount == 0)
            return;

        Focus();
        _dragStart = e.GetPosition(this);
        var frameIndex = controller.SnapFrameFromViewportX(_dragStart.X);
        var modifiers = Keyboard.Modifiers;
        var extendRange = modifiers.HasFlag(ModifierKeys.Shift);
        var toggle = modifiers.HasFlag(ModifierKeys.Control);
        var canMoveSelection = controller.IsFrameSelected(frameIndex) &&
            extendRange == false &&
            toggle == false;
        if (canMoveSelection)
        {
            _dragMode = DragMode.PendingMove;
        }
        else
        {
            controller.SelectFrame(frameIndex, extendRange, toggle);
            _extendBoxSelection = toggle;
            _dragMode = DragMode.PendingBox;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var controller = Controller;
        if (controller == null ||
            IsMouseCaptured == false ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        var horizontalDistance = current.X - _dragStart.X;
        if (_dragMode is DragMode.PendingMove or DragMode.PendingBox &&
            Math.Abs(horizontalDistance) < MinimumDragDistance)
        {
            return;
        }

        if (_dragMode == DragMode.PendingMove)
        {
            var begin = controller.BeginMoveSelection();
            Publish(begin);
            if (begin.Succeeded == false)
            {
                _dragMode = DragMode.None;
                ReleaseMouseCapture();
                return;
            }
            _dragMode = DragMode.Move;
        }
        else if (_dragMode == DragMode.PendingBox)
        {
            _dragMode = DragMode.Box;
        }

        if (_dragMode == DragMode.Move)
        {
            var preview = controller.PreviewMoveSelectionByPixels(
                horizontalDistance);
            Publish(preview);
            if (preview.Succeeded == false)
            {
                controller.CancelMoveSelection();
                _dragMode = DragMode.None;
                ReleaseMouseCapture();
            }
        }
        else if (_dragMode == DragMode.Box)
        {
            controller.BoxSelect(
                _dragStart.X,
                current.X,
                _extendBoxSelection);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var controller = Controller;
        if (controller == null)
            return;

        if (_dragMode == DragMode.Box)
        {
            controller.BoxSelect(
                _dragStart.X,
                e.GetPosition(this).X,
                _extendBoxSelection);
        }
        else if (_dragMode == DragMode.Move)
        {
            Publish(controller.CommitMoveSelection());
        }

        _dragMode = DragMode.None;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragMode == DragMode.Move &&
            Controller?.HasActivePreview == true)
        {
            Publish(Controller.CancelMoveSelection());
        }
        _dragMode = DragMode.None;
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var track = (AnimationWorkbenchTimelineTrack)dependencyObject;
        track.UnsubscribeController(
            args.OldValue as AnimationWorkbenchTimelineController);
        if (track.IsLoaded)
            track.SubscribeController();
        track.InvalidateVisual();
    }

    private void SubscribeController()
    {
        if (_controllerSubscribed || Controller == null)
            return;
        Controller.Changed += OnControllerStateChanged;
        _controllerSubscribed = true;
    }

    private void UnsubscribeController(
        AnimationWorkbenchTimelineController? controller)
    {
        if (_controllerSubscribed == false || controller == null)
            return;
        controller.Changed -= OnControllerStateChanged;
        _controllerSubscribed = false;
    }

    private void OnControllerStateChanged(object? sender, EventArgs args) =>
        InvalidateVisual();

    private void DrawSelectionRange(
        DrawingContext drawingContext,
        AnimationWorkbenchTimelineController controller,
        Brush selectionBrush)
    {
        var range = controller.SelectedRange;
        if (range == null)
            return;
        var start = range.Value.StartFrame * controller.PixelsPerFrame -
            controller.HorizontalOffset;
        var end = range.Value.EndFrameExclusive * controller.PixelsPerFrame -
            controller.HorizontalOffset;
        var left = Math.Max(0, start);
        var right = Math.Min(ActualWidth, end);
        if (right <= left)
            return;
        drawingContext.PushOpacity(0.32);
        drawingContext.DrawRectangle(
            selectionBrush,
            null,
            new Rect(left, 0, right - left, ActualHeight));
        drawingContext.Pop();
    }

    private void DrawRuler(
        DrawingContext drawingContext,
        AnimationWorkbenchTimelineController controller,
        Brush borderBrush,
        Brush textBrush)
    {
        var framesPerLabel = Math.Max(
            1,
            (int)Math.Ceiling(72 / controller.PixelsPerFrame));
        var firstVisibleFrame = Math.Max(
            0,
            (int)Math.Floor(
                controller.HorizontalOffset / controller.PixelsPerFrame));
        var firstLabel = firstVisibleFrame - firstVisibleFrame % framesPerLabel;
        var fontFamily = TryFindResource("AppFontFamily") as FontFamily ??
            SystemFonts.MessageFontFamily;
        var typeface = new Typeface(
            fontFamily,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        for (var frameIndex = firstLabel;
             frameIndex < controller.Timeline.FrameCount;
             frameIndex += framesPerLabel)
        {
            var x = FrameCenterX(controller, frameIndex);
            if (x > ActualWidth)
                break;
            if (x < 0)
                continue;
            drawingContext.DrawLine(
                new Pen(borderBrush, 1),
                new Point(x, ActualHeight - 11),
                new Point(x, ActualHeight));
            var label = new FormattedText(
                frameIndex.ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(label, new Point(x + 3, 2));
        }
    }

    private static double FrameCenterX(
        AnimationWorkbenchTimelineController controller,
        int frameIndex) =>
        (frameIndex + 0.5) * controller.PixelsPerFrame -
        controller.HorizontalOffset;

    private Brush FindBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private void Publish(AnimationWorkbenchTimelineEditResult result) =>
        EditResultAvailable?.Invoke(
            this,
            new AnimationWorkbenchTimelineEditResultEventArgs(result));

    private enum DragMode
    {
        None,
        PendingMove,
        PendingBox,
        Move,
        Box,
    }
}
