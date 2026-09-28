using System;
using System.Windows;
using AssetEditor.Services;
using CommonControls;
using WindowHandling;

namespace AssetEditor.Views.ExternalPack
{
    public partial class ExternalPackOpenChoiceWindow : Window
    {
        public ExternalPackOpenChoice Choice { get; private set; } =
            ExternalPackOpenChoice.Cancelled;

        public ExternalPackOpenChoiceWindow(string packPath)
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            PackPathTextBox.Text = packPath;
            AssetEditorWindow.SetOwnerToActiveWindow(this);
        }

        private void OpenAsReference_Click(
            object sender,
            RoutedEventArgs e)
        {
            Choice = ExternalPackOpenChoice.OpenAsReference;
            DialogResult = true;
        }

        private void ImportAsProject_Click(
            object sender,
            RoutedEventArgs e)
        {
            Choice = ExternalPackOpenChoice.ImportAsFolderProject;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
