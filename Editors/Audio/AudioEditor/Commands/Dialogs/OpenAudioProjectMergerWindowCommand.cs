using System;
using Editors.Audio.AudioProjectMerger;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Events;
using System.Windows;

namespace Editors.Audio.AudioEditor.Commands.Dialogs
{
    public class OpenAudioProjectMergerWindowCommand(IServiceProvider serviceProvider) : IUiCommand
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public void Execute()
        {
            using var scope = _serviceProvider.CreateScope();
            var window = scope.ServiceProvider
                .GetRequiredService<AudioProjectMergerWindow>();
            var viewModel = scope.ServiceProvider
                .GetRequiredService<AudioProjectMergerViewModel>();
            viewModel.SetCloseAction(window.Close);
            window.DataContext = viewModel;
            window.Owner = Application.Current?.MainWindow;
            window.ShowDialog();
        }
    }
}
