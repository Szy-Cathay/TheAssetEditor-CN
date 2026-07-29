using CommonControls;
﻿using System.Windows;
using System.ComponentModel;

namespace Editors.Audio.AudioEditor.Presentation.NewAudioProject
{
    public partial class NewAudioProjectWindow : Window
    {
        public NewAudioProjectWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            Loaded += NewAudioProjectWindow_Loaded;
        }

        private void NewAudioProjectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewAudioProjectViewModel viewModel)
                viewModel.SetCloseAction(this.Close);
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is not NewAudioProjectViewModel
                {
                    IsCreating: true
                } viewModel)
            {
                return;
            }

            viewModel.CancelOrCloseCommand.Execute(null);
            e.Cancel = true;
        }
    }
}
