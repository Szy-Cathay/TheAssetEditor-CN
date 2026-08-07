using System;
using Shared.Core.Events;
using AssetEditor.ViewModels;
using AssetEditor.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace AssetEditor.UiCommands
{
    public class OpenSettingsDialogCommand : IUiCommand
    {
        private readonly IServiceProvider _serviceProvider;

        public OpenSettingsDialogCommand(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Execute()
        {
            var owner = Application.Current.MainWindow;
            var window = _serviceProvider.GetRequiredService<SettingsWindow>();
            window.Owner = owner;
            window.DataContext = _serviceProvider.GetRequiredService<SettingsViewModel>();
            window.ShowDialog();
        }
    }
}
