using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Editors.KitbasherEditor.Commands
{
    internal enum PrimitiveType
    {
        Box,
        Plane,
        Sphere
    }

    internal class ConstructPrimitiveCommand : IRedoableCommand
    {
        private readonly SceneManager _sceneManager;
        private readonly SelectionManager _selectionManager;
        private readonly CapabilityMaterialFactory _capabilityMaterialFactory;
        private readonly PrimitiveConstructor _primitiveConstructor;

        private PrimitiveType _primitiveType = PrimitiveType.Box;
        private ISelectionState? _oldSelectionState;
        private MainEditableNode? _rootNode;
        private Rmv2LodNode? _targetLod;
        private Rmv2MeshNode? _createdMeshNode;
        private bool _createdLod;

        public string HintText =>
            LocalizationManager.Instance?.Get("Kitbash.CommandHint.ConstructPrimitive") ??
            "创建基础几何体";
        public bool IsMutation => true;

        public ConstructPrimitiveCommand(
            SceneManager sceneManager,
            SelectionManager selectionManager,
            CapabilityMaterialFactory capabilityMaterialFactory,
            PrimitiveConstructor primitiveConstructor)
        {
            _sceneManager = sceneManager;
            _selectionManager = selectionManager;
            _capabilityMaterialFactory = capabilityMaterialFactory;
            _primitiveConstructor = primitiveConstructor;
        }

        public void Configure(PrimitiveType primitiveType)
        {
            _primitiveType = primitiveType;
        }

        public void Execute()
        {
            _oldSelectionState = _selectionManager.GetStateCopy();
            _rootNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel) ??
                throw new InvalidOperationException("The editable model node is missing.");

            _targetLod = _rootNode
                .GetLodNodes()
                .FirstOrDefault(x => x.LodValue == 0);
            if (_targetLod == null)
            {
                _targetLod = _rootNode.AddObject(new Rmv2LodNode("Lod 0", 0));
                _createdLod = true;
            }

            var templateMesh = _targetLod.GetAllModels(false).FirstOrDefault();
            var template = BuildTemplate(templateMesh, _rootNode);
            var geometry = _primitiveType switch
            {
                PrimitiveType.Box => _primitiveConstructor.CreateBox(
                    template.VertexFormat,
                    template.SkeletonName),
                PrimitiveType.Plane => _primitiveConstructor.CreatePlane(
                    template.VertexFormat,
                    template.SkeletonName),
                PrimitiveType.Sphere => _primitiveConstructor.CreateSphere(
                    template.VertexFormat,
                    template.SkeletonName),
                _ => throw new NotImplementedException($"Unknown primitive {_primitiveType}")
            };

            var nodeName = GetNodeName(_primitiveType);
            var rmvMaterial = CreatePlainMaterial(template.VertexFormat, nodeName);
            var capabilityMaterial = _capabilityMaterialFactory.Create(rmvMaterial, null);
            _createdMeshNode = new Rmv2MeshNode(
                geometry,
                rmvMaterial,
                capabilityMaterial,
                template.AnimationPlayer!)
            {
                Name = nodeName
            };
            _targetLod.AddObject(_createdMeshNode);
            SelectCreatedMesh();
        }

        public void Undo()
        {
            if (_targetLod != null &&
                _createdMeshNode != null &&
                _targetLod.Children.Contains(_createdMeshNode))
            {
                _targetLod.RemoveObject(_createdMeshNode);
            }

            if (_createdLod &&
                _rootNode != null &&
                _targetLod != null &&
                _targetLod.Children.Count == 0 &&
                _rootNode.Children.Contains(_targetLod))
            {
                _rootNode.RemoveObject(_targetLod);
            }

            if (_oldSelectionState != null)
                _selectionManager.SetState(_oldSelectionState);
        }

        public void Redo()
        {
            if (_rootNode == null || _targetLod == null || _createdMeshNode == null)
                throw new InvalidOperationException("The primitive has not been created.");

            if (_createdLod && _rootNode.Children.Contains(_targetLod) == false)
                _rootNode.AddObject(_targetLod);

            if (_targetLod.Children.Contains(_createdMeshNode) == false)
                _targetLod.AddObject(_createdMeshNode);

            SelectCreatedMesh();
        }

        private static PrimitiveTemplate BuildTemplate(
            Rmv2MeshNode? templateMesh,
            MainEditableNode rootNode)
        {
            if (templateMesh != null)
            {
                return new PrimitiveTemplate(
                    templateMesh.Geometry.VertexFormat,
                    templateMesh.Geometry.SkeletonName,
                    templateMesh.AnimationPlayer);
            }

            var skeleton = rootNode.SkeletonNode?.Skeleton;
            return skeleton == null
                ? new PrimitiveTemplate(UiVertexFormat.Static, string.Empty, null)
                : new PrimitiveTemplate(
                    UiVertexFormat.Weighted,
                    skeleton.SkeletonName,
                    skeleton.AnimationPlayer);
        }

        private static IRmvMaterial CreatePlainMaterial(
            UiVertexFormat vertexFormat,
            string nodeName)
        {
            var material = MaterialFactory
                .Create()
                .CreateMaterial(ModelMaterialEnum.weighted);
            material.ModelName = nodeName;
            material.PivotPoint = Vector3.Zero;
            material.UpdateInternalState(vertexFormat);
            return material;
        }

        private void SelectCreatedMesh()
        {
            var selection = new ObjectSelectionState();
            selection.ModifySelectionSingleObject(_createdMeshNode!, false);
            _selectionManager.SetState(selection);
        }

        private static string GetNodeName(PrimitiveType primitiveType)
        {
            return primitiveType switch
            {
                PrimitiveType.Box => "primitive_box",
                PrimitiveType.Plane => "primitive_plane",
                PrimitiveType.Sphere => "primitive_sphere",
                _ => throw new NotImplementedException($"Unknown primitive {primitiveType}")
            };
        }

        private sealed record PrimitiveTemplate(
            UiVertexFormat VertexFormat,
            string SkeletonName,
            AnimationPlayer? AnimationPlayer);
    }
}
