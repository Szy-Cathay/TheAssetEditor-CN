using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchTimelineView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchTimelineController),
            typeof(AnimationWorkbenchTimelineView),
            new PropertyMetadata(null, OnControllerChanged));

    private bool _updatingView;
    private bool _controllerSubscribed;

    public AnimationWorkbenchTimelineView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchTimelineController? Controller
    {
        get => (AnimationWorkbenchTimelineController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (AnimationWorkbenchTimelineView)dependencyObject;
        view.UnsubscribeController(
            args.OldValue as AnimationWorkbenchTimelineController);
        if (view.IsLoaded)
            view.SubscribeController();
        view.UpdateView();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeController();
        if (string.IsNullOrWhiteSpace(LoopCountTextBox.Text))
            LoopCountTextBox.Text = "2";
        if (string.IsNullOrWhiteSpace(TargetFrameCountTextBox.Text))
        {
            TargetFrameCountTextBox.Text = Math.Max(
                    1,
                    Controller?.Timeline.FrameCount ?? 1)
                .ToString(CultureInfo.CurrentCulture);
        }
        UpdateView();
        Focus();
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e) =>
        UnsubscribeController(Controller);

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
            UpdateView();
    }

    private void SubscribeController()
    {
        if (_controllerSubscribed || Controller == null)
            return;
        Controller.Changed += Controller_Changed;
        _controllerSubscribed = true;
    }

    private void UnsubscribeController(
        AnimationWorkbenchTimelineController? controller)
    {
        if (_controllerSubscribed == false || controller == null)
            return;
        controller.Changed -= Controller_Changed;
        _controllerSubscribed = false;
    }

    private void Controller_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateView();
        }
        else
        {
            Dispatcher.BeginInvoke(UpdateView);
        }
    }

    private void UpdateView()
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;

        _updatingView = true;
        try
        {
            var viewportWidth = Math.Max(0, RulerTrack.ActualWidth);
            var contentWidth =
                Controller.Timeline.FrameCount * Controller.PixelsPerFrame;
            HorizontalScroll.Maximum = Math.Max(
                0,
                contentWidth - viewportWidth);
            HorizontalScroll.ViewportSize = viewportWidth;
            HorizontalScroll.LargeChange = Math.Max(24, viewportWidth * 0.8);
            HorizontalScroll.SmallChange = Controller.PixelsPerFrame;
            HorizontalScroll.Value = Math.Clamp(
                Controller.HorizontalOffset,
                HorizontalScroll.Minimum,
                HorizontalScroll.Maximum);
            ZoomSlider.Value = Controller.Zoom;
            AnchorModeButton.IsChecked = Controller.DisplayMode ==
                AnimationWorkbenchTimelineDisplayMode.EditingAnchors;
            SampleModeButton.IsChecked = Controller.DisplayMode ==
                AnimationWorkbenchTimelineDisplayMode.AllSamples;
            Controller.SetViewport(viewportWidth, HorizontalScroll.Value);
            UndoButton.IsEnabled = Controller.CanUndo &&
                Controller.HasActivePreview == false;
            RedoButton.IsEnabled = Controller.CanRedo &&
                Controller.HasActivePreview == false;
            ApplyPreviewButton.IsEnabled = Controller.HasActivePreview;
            CancelPreviewButton.IsEnabled = Controller.HasActivePreview;
        }
        finally
        {
            _updatingView = false;
        }
    }

    private void AnchorModeButton_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingView || Controller == null)
            return;
        Controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.EditingAnchors);
    }

    private void SampleModeButton_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingView || Controller == null)
            return;
        Controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);
    }

    private void ZoomSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.ZoomAt(e.NewValue, RulerTrack.ActualWidth / 2);
    }

    private void HorizontalScroll_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.SetViewport(RulerTrack.ActualWidth, e.NewValue);
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            ShowResult(Controller.Undo());
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            ShowResult(Controller.Redo());
    }

    private void TrimButton_Click(object sender, RoutedEventArgs e) =>
        Perform(controller => controller.PreviewTrimSelection());

    private void SplitLeadingButton_Click(
        object sender,
        RoutedEventArgs e) => Perform(controller => controller.PreviewSplit(
            AnimationWorkbenchSplitPart.Leading));

    private void SplitTrailingButton_Click(
        object sender,
        RoutedEventArgs e) => Perform(controller => controller.PreviewSplit(
            AnimationWorkbenchSplitPart.Trailing));

    private void ReverseButton_Click(object sender, RoutedEventArgs e) =>
        Perform(controller => controller.PreviewReverseSelection());

    private void LoopButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryReadPositiveInteger(LoopCountTextBox, out var repeatCount))
            Perform(controller => controller.PreviewLoopSelection(repeatCount));
    }

    private void StretchButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryReadPositiveInteger(
                TargetFrameCountTextBox,
                out var targetFrameCount))
        {
            Perform(controller => controller.PreviewStretchSelection(
                targetFrameCount));
        }
    }

    private void InterpolateButton_Click(object sender, RoutedEventArgs e) =>
        Perform(controller => controller.PreviewInterpolateSelection());

    private void MirrorButton_Click(object sender, RoutedEventArgs e) =>
        Perform(controller => controller.PreviewMirrorSelection());

    private void ApplyPreviewButton_Click(
        object sender,
        RoutedEventArgs e) => Perform(controller => controller.CommitPreview());

    private void CancelPreviewButton_Click(
        object sender,
        RoutedEventArgs e) => Perform(controller => controller.CancelPreview());

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Controller == null || e.OriginalSource is TextBox)
            return;

        var extendRange = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Left)
        {
            Controller.NavigateSelection(-1, extendRange);
        }
        else if (e.Key == Key.Right)
        {
            Controller.NavigateSelection(1, extendRange);
        }
        else if (control && e.Key == Key.Z)
        {
            ShowResult(Controller.Undo());
        }
        else if (control && e.Key == Key.Y)
        {
            ShowResult(Controller.Redo());
        }
        else if (e.Key == Key.Enter && Controller.HasActivePreview)
        {
            ShowResult(Controller.CommitPreview());
        }
        else if (e.Key == Key.Escape && Controller.HasActivePreview)
        {
            ShowResult(Controller.CancelPreview());
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void Track_EditResultAvailable(
        object? sender,
        AnimationWorkbenchTimelineEditResultEventArgs e) =>
        ShowResult(e.Result);

    private void Perform(
        Func<AnimationWorkbenchTimelineController,
            AnimationWorkbenchTimelineEditResult> action)
    {
        if (Controller != null)
            ShowResult(action(Controller));
    }

    private void ShowResult(AnimationWorkbenchTimelineEditResult result)
    {
        StatusText.Text = result.Succeeded
            ? Localize(result.State.HasActiveTimelinePreview
                ? "AnimationWorkbench.Timeline.PreviewReady"
                : "AnimationWorkbench.Timeline.EditApplied")
            : Localize(result.Diagnostics.First().ReasonKey);
        UpdateView();
    }

    private void ShowResult(AnimationWorkbenchPoseEditResult result)
    {
        StatusText.Text = result.Succeeded
            ? Localize("AnimationWorkbench.Timeline.EditApplied")
            : Localize(result.Diagnostics.First().ReasonKey);
        UpdateView();
    }

    private bool TryReadPositiveInteger(TextBox textBox, out int value)
    {
        if (int.TryParse(
                textBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out value) &&
            value > 0)
        {
            return true;
        }

        StatusText.Text = Localize(
            "AnimationWorkbench.Timeline.PositiveIntegerRequired");
        textBox.Focus();
        textBox.SelectAll();
        return false;
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
