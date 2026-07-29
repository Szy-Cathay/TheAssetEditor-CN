using System;
using System.Windows;
using CommonControls;
using Editors.Audio.AudioProjectConverter;

namespace Editors.Audio.AudioProjectMerger
{
    public partial class AudioProjectMergerWindow : Window
    {
        public AudioProjectMergerWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            Loaded += AudioProjectConverterWindowLoaded;
        }

        private void AudioProjectConverterWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AudioProjectMergerViewModel viewModel)
                viewModel.SetCloseAction(Close);
        }

    }
}
