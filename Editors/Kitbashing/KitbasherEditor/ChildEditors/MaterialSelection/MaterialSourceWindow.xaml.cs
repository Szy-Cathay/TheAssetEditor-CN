using System.Windows;
using System.Windows.Controls;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Components;
using Shared.Core.Services;
using WindowHandling;

namespace Editors.KitbasherEditor.ChildEditors.MaterialSelection
{
    public partial class MaterialSourceWindow : AssetEditorWindow
    {
        private List<MaterialSourceEntry> _sources = [];
        public Rmv2MeshNode? SelectedMesh => (SourceList.SelectedItem as MaterialSourceEntry)?.Mesh;

        public MaterialSourceWindow()
        {
            InitializeComponent();
        }

        public void Initialize(IEnumerable<Rmv2MeshNode> meshes, int targetCount)
        {
            _sources = meshes.Select(MaterialSourceEntry.FromMesh).ToList();
            TargetDescription.Text = LocalizationManager.Instance.GetFormat("Kitbash.AssignMaterial.TargetDescription", targetCount);
            RefreshFilter();
        }

        private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

        private void RefreshFilter()
        {
            if (SourceList == null)
                return;
            var selected = SourceList.SelectedItem as MaterialSourceEntry;
            var filtered = _sources.Where(source => source.Matches(SearchBox.Text.Trim())).ToList();
            SourceList.ItemsSource = filtered;
            SourceList.SelectedItem = selected != null && filtered.Contains(selected) ? selected : null;
            EmptyMessage.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConfirmButton != null)
                ConfirmButton.IsEnabled = SelectedMesh != null;
        }

        private void ConfirmClick(object sender, RoutedEventArgs e)
        {
            if (SelectedMesh != null)
                DialogResult = true;
        }
    }

    public record MaterialSourceEntry(Rmv2MeshNode Mesh, string Name, string Location, string TexturePath)
    {
        public string TextureName => string.IsNullOrEmpty(TexturePath)
            ? LocalizationManager.Instance.Get("Kitbash.AssignMaterial.NoTexture")
            : System.IO.Path.GetFileName(TexturePath.Replace('\\', '/'));

        public bool Matches(string text) => Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Location.Contains(text, StringComparison.OrdinalIgnoreCase)
            || TexturePath.Contains(text, StringComparison.OrdinalIgnoreCase);

        public static MaterialSourceEntry FromMesh(Rmv2MeshNode mesh)
        {
            var path = new Stack<string>();
            for (var node = mesh.Parent; node?.Parent != null; node = node.Parent)
                path.Push(node switch
                {
                    MainEditableNode => LocalizationManager.Instance.Get("Kitbash.Scene.EditableModel"),
                    Rmv2LodNode lod when node.Name == $"Lod {lod.LodValue}" =>
                        LocalizationManager.Instance.GetFormat("Kitbash.Scene.Lod", lod.LodValue),
                    GroupNode when node.Name == SpecialNodes.ReferenceMeshs =>
                        LocalizationManager.Instance.Get("Kitbash.Scene.References"),
                    _ => node.Name,
                });
            var texture = mesh.Material.TryGetCapability<MetalRoughCapability>()?.BaseColour.TexturePath
                ?? mesh.Material.TryGetCapability<SpecGlossCapability>()?.DiffuseMap.TexturePath ?? "";
            return new(mesh, mesh.Name, string.Join(" / ", path), texture);
        }
    }
}
