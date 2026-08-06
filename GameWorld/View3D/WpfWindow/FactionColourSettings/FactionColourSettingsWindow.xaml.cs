using System.Windows;
using WindowHandling;

namespace GameWorld.Core.WpfWindow.FactionColourSettings
{
    public partial class FactionColourSettingsWindow : AssetEditorWindow
    {
        public FactionColourSettingsWindow(
            FactionColourSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DialogResult = true;
        }
    }
}
