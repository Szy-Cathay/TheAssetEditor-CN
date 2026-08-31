using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchRetargetView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchRetargetController),
            typeof(AnimationWorkbenchRetargetView),
            new PropertyMetadata(null, OnControllerChanged));

    private bool _updatingView;
    private bool _controllerSubscribed;
    private ICollectionView? _mappingView;

    public AnimationWorkbenchRetargetView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchRetargetController? Controller
    {
        get => (AnimationWorkbenchRetargetController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (AnimationWorkbenchRetargetView)dependencyObject;
        var oldController = args.OldValue as
            AnimationWorkbenchRetargetController;
        view.UnsubscribeController(oldController);
        oldController?.ReleasePreview();
        view.BindMappings();
        if (view.IsLoaded)
            view.SubscribeController();
        view.UpdateView();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        BindMappings();
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
        AnimationWorkbenchRetargetController? controller)
    {
        if (!_controllerSubscribed || controller == null)
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

    private void BindMappings()
    {
        DataContext = Controller;
        _mappingView = Controller == null
            ? null
            : CollectionViewSource.GetDefaultView(Controller.Mappings);
        if (_mappingView != null)
            _mappingView.Filter = FilterMapping;
        MappingList.ItemsSource = _mappingView;
    }

    private bool FilterMapping(object item)
    {
        if (item is not AnimationWorkbenchRetargetBoneMapping mapping)
            return false;
        if (UnmappedOnlyCheckBox.IsChecked == true &&
            mapping.SourceBoneIndex.HasValue)
        {
            return false;
        }

        var search = SearchTextBox.Text.Trim();
        return search.Length == 0 ||
            mapping.TargetBoneName.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase) ||
            (mapping.SourceBoneName?.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void UpdateView()
    {
        if (_updatingView || Controller == null || !IsLoaded)
            return;
        _updatingView = true;
        try
        {
            var mappings = Controller.Mappings;
            var mappedCount = mappings.Count(item =>
                item.SourceBoneIndex.HasValue);
            var coreCount = mappings.Count(item =>
                item.IsCoreBone && item.SourceBoneIndex.HasValue);
            var totalCore = mappings.Count(item => item.IsCoreBone);
            MappingCountText.Text = string.Format(
                Localize("AnimationWorkbench.Retarget.CountFormat"),
                mappings.Count);
            MappedCountText.Text = mappedCount.ToString();
            UnmappedCountText.Text = (mappings.Count - mappedCount).ToString();
            CoreCountText.Text = string.Format(
                Localize("AnimationWorkbench.Retarget.CoreCountFormat"),
                coreCount,
                totalCore);
            ProfileStateText.Text = Controller.HasLoadedProfile
                ? Localize("AnimationWorkbench.Retarget.ProfileLoaded")
                : Localize("AnimationWorkbench.Retarget.ProfileUnsaved");
            ApplyButton.IsEnabled = Controller.CanCommit;
            CancelButton.IsEnabled = Controller.HasActivePreview;
            HeaderStateText.Text = Controller.HasActivePreview
                ? Localize("AnimationWorkbench.Retarget.LivePreview")
                : Localize("AnimationWorkbench.Retarget.Committed");
            DiagnosticText.Text = Controller.Diagnostics.Count == 0
                ? Localize("AnimationWorkbench.Retarget.NoDiagnostics")
                : string.Join(
                    Environment.NewLine,
                    Controller.Diagnostics.Select(item =>
                        $"• {Localize(item.ReasonKey)}"));
            StatusText.Text = Controller.LastResult?.Succeeded == false &&
                Controller.Diagnostics.Count != 0
                    ? Localize(Controller.Diagnostics.First().ReasonKey)
                    : Controller.HasActivePreview
                        ? Localize(
                            "AnimationWorkbench.Retarget.PreviewReady")
                        : Localize(
                            "AnimationWorkbench.Retarget.EditApplied");
            _mappingView?.Refresh();
        }
        finally
        {
            _updatingView = false;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) =>
        _mappingView?.Refresh();

    private void SourceBoneComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingView ||
            Controller == null ||
            sender is not ComboBox comboBox ||
            comboBox.DataContext is not
                AnimationWorkbenchRetargetBoneMapping mapping)
        {
            return;
        }

        Controller.SetMapping(
            mapping.TargetBoneIndex,
            comboBox.SelectedValue as int?);
    }

    private void AutoMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null)
            return;
        Controller.ResetAutomaticMapping();
        _mappingView?.Refresh();
        UpdateView();
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null)
            return;
        var result = Controller.SaveProfile();
        StatusText.Text = result.Succeeded
            ? Localize("AnimationWorkbench.Retarget.ProfileSaved")
            : Localize(result.Diagnostics.First().ReasonKey);
    }

    private void RefreshPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Controller?.RefreshPreview();
        UpdateView();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null)
            return;
        var result = Controller.CommitPreview();
        StatusText.Text = result.Succeeded
            ? Localize("AnimationWorkbench.Retarget.EditApplied")
            : Localize(result.Diagnostics.First().ReasonKey);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Controller?.ReleasePreview();
        StatusText.Text = Localize(
            "AnimationWorkbench.Retarget.PreviewCancelled");
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Controller == null ||
            !ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }
        if (e.Key == Key.Enter && Controller.CanCommit)
            ApplyButton_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.Escape && Controller.HasActivePreview)
            CancelButton_Click(this, new RoutedEventArgs());
        else
            return;
        e.Handled = true;
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
