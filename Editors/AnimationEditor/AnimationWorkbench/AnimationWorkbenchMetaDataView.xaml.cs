using System.Windows;
using System.Windows.Controls;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchMetaDataView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchMetaDataController),
            typeof(AnimationWorkbenchMetaDataView),
            new PropertyMetadata(null, OnControllerChanged));

    private bool _updatingView;

    public AnimationWorkbenchMetaDataView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchMetaDataController? Controller
    {
        get => (AnimationWorkbenchMetaDataController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private static void OnControllerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var view = (AnimationWorkbenchMetaDataView)dependencyObject;
        if (args.OldValue is AnimationWorkbenchMetaDataController oldController)
            oldController.Changed -= view.Controller_Changed;
        if (view.IsLoaded &&
            args.NewValue is AnimationWorkbenchMetaDataController newController)
        {
            newController.Changed += view.Controller_Changed;
        }
        view.UpdateView();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            Controller.Changed += Controller_Changed;
        UpdateView();
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Controller != null)
            Controller.Changed -= Controller_Changed;
    }

    private void Controller_Changed(object? sender, EventArgs e) =>
        UpdateView();

    private void UpdateView()
    {
        _updatingView = true;
        try
        {
            DataContext = Controller;
            SynchronizationToggle.IsChecked =
                Controller?.IsSynchronizationEnabled == true;
            NavigateButton.IsEnabled =
                ProblemList.SelectedItem is
                    AnimationWorkbenchMetaDataProblemItem
                    { CanNavigate: true };
        }
        finally
        {
            _updatingView = false;
        }
    }

    private void SynchronizationToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingView || Controller == null)
            return;
        if (!Controller.SetSynchronizationEnabled(
                SynchronizationToggle.IsChecked == true))
        {
            StatusText.Text = Localize(
                "AnimationWorkbench.MetaData.DocumentChanged");
        }
    }

    private void ProblemList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        NavigateButton.IsEnabled = ProblemList.SelectedItem is
            AnimationWorkbenchMetaDataProblemItem { CanNavigate: true };
    }

    private void NavigateButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller == null ||
            ProblemList.SelectedItem is not
                AnimationWorkbenchMetaDataProblemItem item)
        {
            return;
        }
        var result = Controller.Navigate(item);
        StatusText.Text = Localize(result.Succeeded
            ? "AnimationWorkbench.MetaData.NavigationReady"
            : "AnimationWorkbench.MetaData.NavigationUnavailable");
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
