using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using CommonControls;
using Shared.Ui.Common.OperationProgress;

namespace Editors.Audio.DialogueEventMerger
{
    public partial class DialogueEventMergerWindow : Window
    {
        private readonly CancellationTokenSource _initializationCancellation =
            new();
        private readonly OperationProgressWindowHost _operationProgressHost;
        private bool _closeWhenIdle;

        public DialogueEventMergerWindow()
        {
            InitializeComponent();
            _operationProgressHost = ProgressSurface.Children
                .OfType<OperationProgressWindowHost>()
                .Single();
            DarkTitleBarHelper.Enable(this);
            Loaded += DialogueEventMergerWindowLoaded;
        }

        private async void DialogueEventMergerWindowLoaded(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is DialogueEventMergerViewModel viewModel)
            {
                viewModel.SetCloseAction(Close);
                viewModel.SetProgressCompletionAction(
                    _operationProgressHost.CompleteAsync);
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                await viewModel.InitializeAsync(
                    _initializationCancellation.Token);
            }
        }

        private void OnClosing(
            object sender,
            CancelEventArgs e)
        {
            if (DataContext is DialogueEventMergerViewModel
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
                    DialogueEventMergerViewModel.IsBusy) ||
                !_closeWhenIdle ||
                sender is not DialogueEventMergerViewModel
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
            if (DataContext is DialogueEventMergerViewModel viewModel)
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _initializationCancellation.Cancel();
            _initializationCancellation.Dispose();
        }
    }
}
