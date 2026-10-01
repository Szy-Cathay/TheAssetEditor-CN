using Shared.Core.Services;
using WindowHandling;

namespace CommonControls.Editors.AnimationPack
{
    public partial class ResourcePreviewWindow : AssetEditorWindow
    {
        public ResourcePreviewWindow(string path, string text)
        {
            InitializeComponent();
            Title = LocalizationManager.Instance.Get("AnimPack.ResourceTitle");
            ResourcePath.Text = path;
            ResourceText.Text = text;
        }
    }
}
