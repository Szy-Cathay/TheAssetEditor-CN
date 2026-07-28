using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;
using Shared.Ui.BaseDialogs.MathViews;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2
{
    public partial class MeshViewModel : ObservableObject
    {
        Rmv2MeshNode _meshNode;
        private readonly SceneManager _sceneManager;
        private readonly KitbasherRootScene _kitbasherRootScene;
        private readonly SceneNodePropertyEditor _propertyEditor;

        public Vector3ViewModel Pivot { get; set; }

        [ObservableProperty] public string _modelName; 
        [ObservableProperty] public bool _drawBoundingBox; 
        [ObservableProperty] public bool _drawPivotPoint;
        [ObservableProperty] bool _reduceMeshOnLodGeneration ;
        [ObservableProperty] int _vertexCount;
        [ObservableProperty] int _indexCount;
        [ObservableProperty] UiVertexFormat _vertexType;
        [ObservableProperty] IEnumerable<UiVertexFormat> _possibleVertexTypes = [UiVertexFormat.Static, UiVertexFormat.Weighted, UiVertexFormat.Cinematic];

        public MeshViewModel(
            KitbasherRootScene kitbasherRootScene,
            SceneManager sceneManager,
            SceneNodePropertyEditor propertyEditor)
        {
            _kitbasherRootScene = kitbasherRootScene;
            _sceneManager = sceneManager;
            _propertyEditor = propertyEditor;
        }

        public void Initialize(Rmv2MeshNode node)
        {
            _meshNode = node;

            Pivot = new Vector3ViewModel(_meshNode.PivotPoint, Pivot_OnValueChanged);
            ModelName = _meshNode.Name;
            DrawBoundingBox = _meshNode.DisplayBoundingBox;
            DrawPivotPoint = _meshNode.DisplayPivotPoint;
      
            VertexCount = _meshNode.Geometry.VertexCount();
            IndexCount = _meshNode.Geometry.GetIndexCount();

            VertexType = _meshNode.Geometry.VertexFormat;
            ReduceMeshOnLodGeneration = _meshNode.ReduceMeshOnLodGeneration;
        }

        partial void OnModelNameChanged(string value) =>
            _propertyEditor.Update(
                _meshNode.Name,
                value,
                newValue => _meshNode.Name = newValue,
                newValue => ModelName = newValue);

        partial void OnDrawBoundingBoxChanged(bool value) => _meshNode.DisplayBoundingBox = value;
        partial void OnDrawPivotPointChanged(bool value) => _meshNode.DisplayPivotPoint = value;

        partial void OnReduceMeshOnLodGenerationChanged(bool value) =>
            _propertyEditor.Update(
                _meshNode.ReduceMeshOnLodGeneration,
                value,
                newValue => _meshNode.ReduceMeshOnLodGeneration = newValue,
                newValue => ReduceMeshOnLodGeneration = newValue);

        partial void OnVertexTypeChanged(UiVertexFormat value) =>
            _propertyEditor.Update(
                _meshNode.Geometry.VertexFormat,
                value,
                newValue => _meshNode.Geometry.ChangeVertexType(newValue),
                newValue => VertexType = newValue);

        private void Pivot_OnValueChanged(Vector3 newValue) =>
            _propertyEditor.Update(
                _meshNode.PivotPoint,
                newValue,
                value => _meshNode.PivotPoint = value,
                value => Pivot.Set(value));

        [RelayCommand]
        void CopyPivotToAllMeshes()
        {
            var newPivot = new Vector3(
                (float)Pivot.X.Value,
                (float)Pivot.Y.Value,
                (float)Pivot.Z.Value);
            var root = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            var allMeshes = root.GetMeshesInLod(0, false);
            if (allMeshes.Count == 0)
                return;

            var oldState = new PivotCollectionState(
                allMeshes
                    .Select(mesh => new PivotValue(mesh, mesh.PivotPoint))
                    .ToArray());
            var newState = new PivotCollectionState(
                allMeshes
                    .Select(mesh => new PivotValue(mesh, newPivot))
                    .ToArray());
            _propertyEditor.Update(oldState, newState, ApplyPivotCollection);
        }

        private void ApplyPivotCollection(PivotCollectionState state)
        {
            foreach (var item in state.Values)
            {
                item.Mesh.PivotPoint = item.Pivot;
                if (ReferenceEquals(item.Mesh, _meshNode))
                    Pivot.Set(item.Pivot);
            }
        }

        private sealed record PivotCollectionState(IReadOnlyList<PivotValue> Values);
        private sealed record PivotValue(Rmv2MeshNode Mesh, Vector3 Pivot);
    }
}
