using System.Windows.Controls;

namespace Editors.Audio.AudioEditor.Presentation.WaveformVisualiser
{
    public partial class WaveformVisualiserView : UserControl
    {
        public WaveformVisualiserViewModel ViewModel => DataContext as WaveformVisualiserViewModel;

        public WaveformVisualiserView()
        {
            InitializeComponent();

            Loaded += (s, e) => (DataContext as WaveformVisualiserViewModel)?.SetSelectedHostWidth(AudioWaveformGrid.ActualWidth);
            AudioWaveformGrid.SizeChanged += (s, e) => (DataContext as WaveformVisualiserViewModel)?.SetSelectedHostWidth(AudioWaveformGrid.ActualWidth);
        }

        private void OnWaveformMouseLeftButtonDown(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AudioWaveformGrid.ActualWidth <= 0)
                return;

            var ratio = e.GetPosition(AudioWaveformGrid).X /
                AudioWaveformGrid.ActualWidth;
            ViewModel?.SeekToRatio(ratio);
        }
    }
}
