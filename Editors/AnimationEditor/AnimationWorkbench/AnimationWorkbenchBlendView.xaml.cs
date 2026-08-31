using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchBlendView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchBlendController),
            typeof(AnimationWorkbenchBlendView),
            new PropertyMetadata(null, OnControllerChanged));

    private bool _updatingView;
    private bool _controllerSubscribed;

    public AnimationWorkbenchBlendView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchBlendController? Controller
    {
        get => (AnimationWorkbenchBlendController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (AnimationWorkbenchBlendView)dependencyObject;
        var oldController = args.OldValue as AnimationWorkbenchBlendController;
        view.UnsubscribeController(oldController);
        oldController?.ReleasePreview();
        if (view.IsLoaded)
            view.SubscribeController();
        view.UpdateView();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeController();
        UpdateView();
        Focus();
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeController(Controller);
        Controller?.ReleasePreview();
    }

    private void SubscribeController()
    {
        if (_controllerSubscribed || Controller == null)
            return;
        Controller.Changed += Controller_Changed;
        _controllerSubscribed = true;
    }

    private void UnsubscribeController(
        AnimationWorkbenchBlendController? controller)
    {
        if (_controllerSubscribed == false || controller == null)
            return;
        controller.Changed -= Controller_Changed;
        _controllerSubscribed = false;
    }

    private void Controller_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            UpdateView();
        else
            Dispatcher.BeginInvoke(UpdateView);
    }

    private void UpdateView()
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        _updatingView = true;
        try
        {
            AnimationAOutSlider.Maximum = Controller.MaximumAnimationAOutFrame;
            AnimationAOutSlider.Value = Math.Clamp(
                Controller.AnimationAOutFrame,
                0,
                Controller.MaximumAnimationAOutFrame);
            AnimationBInSlider.Maximum = Controller.MaximumAnimationBInFrame;
            AnimationBInSlider.Value = Math.Clamp(
                Controller.AnimationBInFrame,
                0,
                Controller.MaximumAnimationBInFrame);
            OverlapSlider.Maximum = Controller.MaximumOverlapSeconds;
            OverlapSlider.Value = Math.Clamp(
                Controller.OverlapSeconds,
                0,
                Controller.MaximumOverlapSeconds);
            AnimationAOutValue.Text = Controller.AnimationAOutFrame
                .ToString(CultureInfo.CurrentCulture);
            AnimationBInValue.Text = Controller.AnimationBInFrame
                .ToString(CultureInfo.CurrentCulture);
            OverlapValue.Text = string.Format(
                CultureInfo.CurrentCulture,
                Localize("AnimationWorkbench.Blend.SecondsFormat"),
                Controller.OverlapSeconds);
            OutputFpsTextBox.Text = Controller.OutputFramesPerSecond
                .ToString("0.###", CultureInfo.CurrentCulture);
            CurveComboBox.SelectedIndex = (int)Controller.Curve;
            AlignHorizontalCheckBox.IsChecked =
                Controller.AlignHorizontalPosition;
            AlignYawCheckBox.IsChecked = Controller.AlignYaw;
            PreserveHeightCheckBox.IsChecked =
                Controller.PreserveSourceHeightChanges;
            ApplyButton.IsEnabled = Controller.CanCommit;
            CancelButton.IsEnabled = Controller.HasActivePreview;

            UpdateImpact(Controller.Impact);
            UpdateDiagnostics(Controller.Diagnostics);
            HeaderStateText.Text = Controller.HasActivePreview
                ? Localize("AnimationWorkbench.Blend.LivePreview")
                : Localize("AnimationWorkbench.Blend.Committed");
        }
        finally
        {
            _updatingView = false;
        }
    }

    private void UpdateImpact(AnimationWorkbenchBlendImpact? impact)
    {
        if (impact == null)
        {
            SourceRatesText.Text = "—";
            OutputRateText.Text = "—";
            OutputFramesText.Text = "—";
            OutputDurationText.Text = "—";
            ResampleText.Text = Localize(
                "AnimationWorkbench.Blend.ImpactUnavailable");
            LoopSeamText.Text = Localize(
                "AnimationWorkbench.Blend.ImpactUnavailable");
            return;
        }

        SourceRatesText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Blend.SourceRatesFormat"),
            impact.AnimationAFramesPerSecond,
            impact.AnimationBFramesPerSecond);
        OutputRateText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Blend.RateFormat"),
            impact.OutputFramesPerSecond);
        OutputFramesText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Blend.OutputFramesFormat"),
            impact.OutputFrameCount,
            impact.AnimationAOutputFrameCount,
            impact.AnimationBOutputFrameCount,
            impact.OverlapFrameCount);
        OutputDurationText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Blend.SecondsFormat"),
            impact.OutputDuration.TotalSeconds);
        var resamplingKey = (impact.AnimationAWasResampled,
            impact.AnimationBWasResampled) switch
        {
            (true, true) => "AnimationWorkbench.Blend.ResamplingAppliedBoth",
            (true, false) => "AnimationWorkbench.Blend.ResamplingAppliedA",
            (false, true) => "AnimationWorkbench.Blend.ResamplingAppliedB",
            _ => "AnimationWorkbench.Blend.ResamplingNotNeeded",
        };
        var quantization = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Blend.OverlapQuantizationFormat"),
            impact.RequestedOverlapDuration.TotalSeconds,
            impact.QuantizedOverlapDuration.TotalSeconds,
            (impact.QuantizedOverlapDuration -
                impact.RequestedOverlapDuration).TotalSeconds);
        ResampleText.Text = $"{Localize(resamplingKey)}{Environment.NewLine}{quantization}";
        LoopSeamText.Text = impact.HasLoopSeamDiscontinuity
            ? string.Format(
                CultureInfo.CurrentCulture,
                Localize("AnimationWorkbench.Blend.LoopSeamWarning"),
                impact.LoopSeamPositionDelta,
                impact.LoopSeamRotationDegrees)
            : Localize("AnimationWorkbench.Blend.LoopSeamContinuous");
    }

    private void UpdateDiagnostics(
        IReadOnlyList<AnimationWorkbenchDiagnostic> diagnostics)
    {
        DiagnosticText.Text = diagnostics.Count == 0
            ? Localize("AnimationWorkbench.Blend.NoDiagnostics")
            : string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic =>
                    $"• {Localize(diagnostic.ReasonKey)}"));
        var failure = Controller?.LastResult?.Succeeded == false;
        StatusText.Text = failure
            ? Localize(diagnostics.First().ReasonKey)
            : Controller?.HasActivePreview == true
                ? Localize("AnimationWorkbench.Blend.PreviewReady")
                : Localize("AnimationWorkbench.Blend.EditApplied");
    }

    private void AnimationAOutSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.AnimationAOutFrame = (int)Math.Round(e.NewValue);
    }

    private void AnimationBInSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.AnimationBInFrame = (int)Math.Round(e.NewValue);
    }

    private void OverlapSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.OverlapSeconds = e.NewValue;
    }

    private void CurveComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingView ||
            Controller == null ||
            CurveComboBox.SelectedIndex < 0)
        {
            return;
        }
        Controller.Curve = (AnimationWorkbenchBlendCurve)
            CurveComboBox.SelectedIndex;
    }

    private void RootMotionCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.AlignHorizontalPosition =
            AlignHorizontalCheckBox.IsChecked == true;
        Controller.AlignYaw = AlignYawCheckBox.IsChecked == true;
        Controller.PreserveSourceHeightChanges =
            PreserveHeightCheckBox.IsChecked == true;
    }

    private void OutputFpsTextBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => ApplyOutputFps();

    private void RefreshPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ApplyOutputFps() && Controller != null)
            ShowResult(Controller.RefreshPreview());
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApplyOutputFps() && Controller != null)
            ShowResult(Controller.CommitPreview());
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            ShowCancelResult(Controller.CancelPreview());
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Controller == null)
            return;
        if (e.Key == Key.Enter &&
            ReferenceEquals(e.OriginalSource, OutputFpsTextBox))
        {
            ApplyOutputFps();
        }
        else if (e.Key == Key.Enter &&
                 ReferenceEquals(e.OriginalSource, this) &&
                 Controller.CanCommit)
        {
            ShowResult(Controller.CommitPreview());
        }
        else if (e.Key == Key.Escape && Controller.HasActivePreview)
        {
            ShowCancelResult(Controller.CancelPreview());
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private bool ApplyOutputFps()
    {
        if (Controller == null)
            return false;
        if (double.TryParse(
                OutputFpsTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var framesPerSecond) &&
            double.IsFinite(framesPerSecond) &&
            framesPerSecond > 0)
        {
            Controller.OutputFramesPerSecond = framesPerSecond;
            return true;
        }

        Controller.ReleasePreview();
        StatusText.Text = Localize(
            "AnimationWorkbench.Blend.PositiveFpsRequired");
        OutputFpsTextBox.Focus();
        OutputFpsTextBox.SelectAll();
        return false;
    }

    private void ShowResult(AnimationWorkbenchBlendResult result)
    {
        StatusText.Text = result.Succeeded
            ? result.State.HasActiveBlendPreview
                ? Localize("AnimationWorkbench.Blend.PreviewReady")
                : Localize("AnimationWorkbench.Blend.EditApplied")
            : Localize(result.Diagnostics.First().ReasonKey);
        UpdateView();
    }

    private void ShowCancelResult(AnimationWorkbenchBlendResult result)
    {
        UpdateView();
        StatusText.Text = result.Succeeded
            ? Localize("AnimationWorkbench.Blend.PreviewCancelled")
            : Localize(result.Diagnostics.First().ReasonKey);
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
