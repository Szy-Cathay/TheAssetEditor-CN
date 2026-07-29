using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.Audio.AudioExplorer
{
    public partial class AudioExplorerView : UserControl
    {
        public AudioExplorerViewModel ViewModel => DataContext as AudioExplorerViewModel;

        public AudioExplorerView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;

            Loaded -= OnLoaded;
            await ViewModel.InitializeAsync();
        }

        private async void OnNodeDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            await ViewModel.PlayAudioAsync();

        private void OnAudioWaveformGridSizeChanged(
            object sender,
            System.Windows.SizeChangedEventArgs e) =>
            ViewModel?.SetWaveformDisplayWidth(e.NewSize.Width);
    }
}
