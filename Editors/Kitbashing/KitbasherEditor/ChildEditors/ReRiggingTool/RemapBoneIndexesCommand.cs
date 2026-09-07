using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.KitbasherEditor.ChildEditors.ReRiggingTool
{
    public class RemapBoneIndexesCommand : IRedoableCommand
    {
        List<IndexRemapping> _mapping;
        string _newSkeletonName;

        List<Rmv2MeshNode> _meshNodeList;
        List<MeshObject> _originalGeometry;
        List<MeshObject> _remappedGeometry;

        public void Configure(List<Rmv2MeshNode> meshNodeList, List<IndexRemapping> mapping, string newSkeletonName)
        {
            _meshNodeList = meshNodeList;
            _mapping = mapping;

            _newSkeletonName = newSkeletonName;
        }

        public string HintText { get => "Remap skeleton"; }
        public bool IsMutation { get => true; }

        public void Execute()
        {
            _originalGeometry = _meshNodeList.Select(x => x.Geometry).ToList();
            _remappedGeometry = _originalGeometry.Select(x => x.Clone()).ToList();
            foreach (var geometry in _remappedGeometry)
            {
                geometry.UpdateAnimationIndecies(_mapping);
                geometry.UpdateSkeletonName(_newSkeletonName);
            }
            Redo();
        }

        public void Redo()
        {
            for (var i = 0; i < _meshNodeList.Count; i++)
                _meshNodeList[i].Geometry = _remappedGeometry[i];
        }

        public void Undo()
        {
            for (var i = 0; i < _meshNodeList.Count; i++)
            {
                _meshNodeList[i].Geometry = _originalGeometry[i];
            }
        }
    }
}
