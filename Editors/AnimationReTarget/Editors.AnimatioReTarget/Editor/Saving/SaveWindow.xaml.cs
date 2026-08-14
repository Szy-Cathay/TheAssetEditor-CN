using System.Windows;
using WindowHandling;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    /// <summary>
    /// Interaction logic for SaveWindow.xaml
    /// </summary>
    public partial class SaveWindow : AssetEditorWindow
    {
        SaveManager _saveManager = null!;

        public SaveWindow(SaveSettings viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        public void Initialize(SaveManager saveManager)
        {
            _saveManager = saveManager;
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            _saveManager.SaveAnimation();
            Close();
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
