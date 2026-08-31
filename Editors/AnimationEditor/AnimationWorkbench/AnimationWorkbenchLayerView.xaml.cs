using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchLayerView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchLayerController),
            typeof(AnimationWorkbenchLayerView),
            new PropertyMetadata(null, OnControllerChanged));

    private bool _updatingView;
    private bool _controllerSubscribed;

    public AnimationWorkbenchLayerView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchLayerController? Controller
    {
        get => (AnimationWorkbenchLayerController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (AnimationWorkbenchLayerView)dependencyObject;
        var oldController = args.OldValue as AnimationWorkbenchLayerController;
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
        AnimationWorkbenchLayerController? controller)
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
            BoneTree.ItemsSource = Controller.RootBones;
            SearchTextBox.Text = Controller.SearchText;
            var selectedMask = SavedMaskComboBox.SelectedItem as string;
            SavedMaskComboBox.ItemsSource = Controller.SavedMaskNames;
            SavedMaskComboBox.SelectedItem =
                Controller.SavedMaskNames.Contains(selectedMask ?? "",
                    StringComparer.Ordinal)
                    ? selectedMask
                    : Controller.SavedMaskNames.FirstOrDefault();
            LoadMaskButton.IsEnabled = SavedMaskComboBox.SelectedItem != null;
            ModeComboBox.SelectedIndex = (int)Controller.Mode;
            ReferenceComboBox.SelectedIndex = (int)Controller.ReferencePose;
            ReferenceComboBox.IsEnabled = Controller.Mode ==
                AnimationWorkbenchLayerMode.Additive;
            WeightSlider.Value = Controller.Weight;
            WeightText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Localize("AnimationWorkbench.Layer.WeightFormat"),
                Controller.Weight);
            SelectedBonesText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Localize("AnimationWorkbench.Layer.SelectedFormat"),
                Controller.SelectedBoneCount,
                Controller.TotalBoneCount);
            ApplyButton.IsEnabled = Controller.CanCommit;
            CancelButton.IsEnabled = Controller.HasActivePreview;
            HeaderStateText.Text = Controller.HasActivePreview
                ? Localize("AnimationWorkbench.Layer.LivePreview")
                : Localize("AnimationWorkbench.Layer.Committed");
            UpdateImpact(Controller.Impact);
            UpdateDiagnostics(Controller.Diagnostics);
        }
        finally
        {
            _updatingView = false;
        }
    }

    private void UpdateImpact(AnimationWorkbenchLayerImpact? impact)
    {
        if (impact == null)
        {
            ImpactBonesText.Text = "—";
            OutputFramesText.Text = "—";
            OutputDurationText.Text = "—";
            ResampleText.Text = Localize(
                "AnimationWorkbench.Layer.ImpactUnavailable");
            return;
        }
        ImpactBonesText.Text = impact.MaskedBoneCount.ToString(
            CultureInfo.CurrentCulture);
        OutputFramesText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Layer.FramesFormat"),
            impact.OutputFrameCount);
        OutputDurationText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localize("AnimationWorkbench.Layer.SecondsFormat"),
            impact.OutputDuration.TotalSeconds);
        ResampleText.Text = Localize(impact.AnimationBWasResampled
            ? "AnimationWorkbench.Layer.Resampled"
            : "AnimationWorkbench.Layer.ResamplingNotNeeded");
    }

    private void UpdateDiagnostics(
        IReadOnlyList<AnimationWorkbenchDiagnostic> diagnostics)
    {
        DiagnosticText.Text = diagnostics.Count == 0
            ? Localize("AnimationWorkbench.Layer.NoDiagnostics")
            : string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic =>
                    $"• {Localize(diagnostic.ReasonKey)}"));
        StatusText.Text = diagnostics.Count != 0
            ? Localize(diagnostics[0].ReasonKey)
            : Controller?.HasActivePreview == true
                ? Localize("AnimationWorkbench.Layer.PreviewReady")
                : Localize("AnimationWorkbench.Layer.EditApplied");
    }

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.SearchText = SearchTextBox.Text;
    }

    private void ModeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingView || Controller == null ||
            ModeComboBox.SelectedIndex < 0)
        {
            return;
        }
        Controller.Mode = (AnimationWorkbenchLayerMode)
            ModeComboBox.SelectedIndex;
    }

    private void ReferenceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingView || Controller == null ||
            ReferenceComboBox.SelectedIndex < 0)
        {
            return;
        }
        Controller.ReferencePose =
            (AnimationWorkbenchAdditiveReferencePose)
                ReferenceComboBox.SelectedIndex;
    }

    private void WeightSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingView || Controller == null || IsLoaded == false)
            return;
        Controller.Weight = e.NewValue;
    }

    private void SaveMaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null)
            return;
        var name = MaskNameTextBox.Text.Trim();
        var result = Controller.SaveMask(name);
        UpdateView();
        if (result.Succeeded)
        {
            SavedMaskComboBox.SelectedItem = name;
            StatusText.Text = Localize("AnimationWorkbench.Layer.MaskSaved");
        }
    }

    private void LoadMaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null ||
            SavedMaskComboBox.SelectedItem is not string name)
        {
            return;
        }
        var result = Controller.LoadMask(name);
        UpdateView();
        if (result.Succeeded)
            StatusText.Text = Localize("AnimationWorkbench.Layer.MaskLoaded");
    }

    private void RefreshPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Controller != null)
            ShowResult(Controller.RefreshPreview());
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            ShowResult(Controller.CommitPreview());
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null)
            return;
        var result = Controller.CancelPreview();
        UpdateView();
        StatusText.Text = result.Succeeded
            ? Localize("AnimationWorkbench.Layer.PreviewCancelled")
            : Localize(result.Diagnostics.First().ReasonKey);
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Controller == null || ReferenceEquals(e.OriginalSource, this) == false)
            return;
        if (e.Key == Key.Enter && Controller.CanCommit)
            ShowResult(Controller.CommitPreview());
        else if (e.Key == Key.Escape && Controller.HasActivePreview)
        {
            var result = Controller.CancelPreview();
            UpdateView();
            StatusText.Text = result.Succeeded
                ? Localize("AnimationWorkbench.Layer.PreviewCancelled")
                : Localize(result.Diagnostics.First().ReasonKey);
        }
        else
            return;
        e.Handled = true;
    }

    private void ShowResult(AnimationWorkbenchLayerResult result)
    {
        StatusText.Text = result.Succeeded
            ? result.State.HasActiveLayerPreview
                ? Localize("AnimationWorkbench.Layer.PreviewReady")
                : Localize("AnimationWorkbench.Layer.EditApplied")
            : Localize(result.Diagnostics.First().ReasonKey);
        UpdateView();
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
