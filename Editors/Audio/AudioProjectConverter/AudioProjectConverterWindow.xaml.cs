using System;
using System.Threading;
using System.ComponentModel;
using System.Windows;
using CommonControls;

namespace Editors.Audio.AudioProjectConverter
{
    public partial class AudioProjectConverterWindow : Window
    {
        private readonly CancellationTokenSource _initializationCancellation =
            new();
        private bool _closeWhenIdle;

        public AudioProjectConverterWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            Loaded += AudioProjectConverterWindowLoaded;
        }

        private async void AudioProjectConverterWindowLoaded(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is AudioProjectConverterViewModel viewModel)
            {
                viewModel.SetCloseAction(Close);
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                await viewModel.InitializeAsync(
                    _initializationCancellation.Token);
            }
        }

        private void OnClosing(
            object sender,
            System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is AudioProjectConverterViewModel
                {
                    IsBusy: true
                })
            {
                _closeWhenIdle = true;
                _initializationCancellation.Cancel();
                e.Cancel = true;
            }
        }

        private void OnViewModelPropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(
                    AudioProjectConverterViewModel.IsBusy) ||
                !_closeWhenIdle ||
                sender is not AudioProjectConverterViewModel
                {
                    IsBusy: false
                })
            {
                return;
            }

            _closeWhenIdle = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (DataContext is AudioProjectConverterViewModel viewModel)
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _initializationCancellation.Cancel();
            _initializationCancellation.Dispose();
        }
    }
}
