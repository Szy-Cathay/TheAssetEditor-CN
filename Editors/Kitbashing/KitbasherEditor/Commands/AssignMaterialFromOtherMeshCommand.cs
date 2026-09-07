using CommunityToolkit.Diagnostics;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using Serilog;
using Shared.Core.ErrorHandling;

namespace Editors.KitbasherEditor.Commands
{
    internal class AssignMaterialFromOtherMeshCommand : IRedoableCommand
    {
        record MeshMaterialHistory(Rmv2MeshNode Mesh, CapabilityMaterial Material);

        readonly ILogger _logger = Logging.Create<AssignMaterialFromOtherMeshCommand>();
        public string HintText => "Assign Material";
        public bool IsMutation => true;

        
        readonly List<MeshMaterialHistory> _history = [];
        List<CapabilityMaterial> _assignedMaterials = [];
        CapabilityMaterial? _newMaterial;

        public void Execute()
        {
            Guard.IsNotNull(_newMaterial, "New material cannot be null when executing AssignMaterialFromOtherMeshCommand"); 

            _assignedMaterials = _history.Select(_ => _newMaterial.Clone()).ToList();
            Redo();
        }

        public void Redo()
        {
            for (var i = 0; i < _history.Count; i++)
                _history[i].Mesh.Material = _assignedMaterials[i];
        }

        public void Configure(CapabilityMaterial materialToAssign, List<Rmv2MeshNode> meshesToAssignTo)
        {
            _logger.Here().Information("Assigning material {MaterialName} to {MeshCount} meshes", meshesToAssignTo.Count, materialToAssign.Type.ToString());

            _newMaterial = materialToAssign;
            foreach (var mesh in meshesToAssignTo)
            { 
                var historyItem = new MeshMaterialHistory(mesh, mesh.Material);
                _history.Add(historyItem);
            }
        }

        public void Undo()
        {
            foreach (var historyItem in _history)
                historyItem.Mesh.Material = historyItem.Material;
        }
    }
}
