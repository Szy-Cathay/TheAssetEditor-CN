using CommonControls;
﻿using System.Windows;
using System.ComponentModel;
using AssetEditor.ViewModels;

namespace AssetEditor.Views.Settings
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            Closing += OnClosing;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel viewModel)
                return;

            viewModel.SaveCommand.Execute(null);
            if (viewModel.IsSaved)
                Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
                viewModel.Cancel();
        }
    }
}
