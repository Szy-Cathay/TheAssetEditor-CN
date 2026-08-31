using System.Windows;
using System.Windows.Controls;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchBaseAnimationView : UserControl
{
    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register(
            nameof(Controller),
            typeof(AnimationWorkbenchBaseAnimationController),
            typeof(AnimationWorkbenchBaseAnimationView));

    public AnimationWorkbenchBaseAnimationView()
    {
        InitializeComponent();
    }

    public AnimationWorkbenchBaseAnimationController? Controller
    {
        get => (AnimationWorkbenchBaseAnimationController?)GetValue(
            ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e) =>
        Controller?.ReleasePreview();
}
