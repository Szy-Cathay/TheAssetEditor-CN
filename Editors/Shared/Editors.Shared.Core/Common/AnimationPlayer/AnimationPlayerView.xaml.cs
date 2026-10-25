using System.Windows;
using System.Windows.Controls;

namespace AnimationEditor.Common.AnimationPlayer
{
    /// <summary>
    /// Interaction logic for AnimationPlayerView.xaml
    /// </summary>
    public partial class AnimationPlayerView : UserControl
    {
        public static readonly DependencyProperty ShowFrameInformationProperty = DependencyProperty.Register(
            nameof(ShowFrameInformation), typeof(bool), typeof(AnimationPlayerView), new PropertyMetadata(true));

        public bool ShowFrameInformation
        {
            get => (bool)GetValue(ShowFrameInformationProperty);
            set => SetValue(ShowFrameInformationProperty, value);
        }

        public AnimationPlayerView()
        {
            InitializeComponent();
        }
    }
}
