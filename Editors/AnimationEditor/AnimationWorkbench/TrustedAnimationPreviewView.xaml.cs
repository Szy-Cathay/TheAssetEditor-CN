using System.Windows.Controls;

using System.Windows;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class TrustedAnimationPreviewView : UserControl
{
    public TrustedAnimationPreviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrustedAnimationPreviewViewModel viewModel &&
            viewModel.IsModelPickerOpen)
        {
            _ = viewModel.StartModelDiscoveryAsync();
        }
    }
}
