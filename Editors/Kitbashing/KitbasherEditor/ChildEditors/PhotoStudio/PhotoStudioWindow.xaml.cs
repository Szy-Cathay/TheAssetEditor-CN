using WindowHandling;

namespace Editors.KitbasherEditor.ChildEditors.PhotoStudio
{
    public partial class PhotoStudioWindow : AssetEditorWindow
    {
        public PhotoStudioWindow(
            PhotoStudioViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
