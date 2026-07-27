using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.PinTool.Commands
{
    public class SkinWrapRiggingCommand : ICommand
    {
        ISelectionState _selectionOldState;
        private readonly SelectionManager _selectionManager;

        List<MeshObject> _originalGeometries;

        List<Rmv2MeshNode> _giveAnimationToList;
        Rmv2MeshNode _takeAnimationFrom;

        public string HintText => LocalizationManager.Instance.Get("Kitbash.CommandHint.SkinWrapRigging");
        public bool IsMutation { get => true; }



        public SkinWrapRiggingCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager; ;
        }

        public void Configure(IEnumerable<Rmv2MeshNode> giveAnimationTo, Rmv2MeshNode takeAnimationFrom)
        {
            _giveAnimationToList = giveAnimationTo.ToList();
            _takeAnimationFrom = takeAnimationFrom;
        }

        public void Execute()
        {
            if (_takeAnimationFrom == null)
                throw new InvalidOperationException("必须先选择源网格。");

            var weightTransfer = RegiggingHelper.CreateWeightTransfer(
                _takeAnimationFrom.Geometry,
                _takeAnimationFrom.ModelMatrix);
            var updatedGeometries = new List<MeshObject>(_giveAnimationToList.Count);
            _originalGeometries = _giveAnimationToList.Select(x => x.Geometry).ToList();
            _selectionOldState = _selectionManager.GetStateCopy();

            foreach (var giveAnimationTo in _giveAnimationToList)
            {
                var updatedGeometry = giveAnimationTo.Geometry.Clone();
                updatedGeometry.ChangeVertexType(_takeAnimationFrom.Geometry.VertexFormat, false);
                updatedGeometry.UpdateSkeletonName(_takeAnimationFrom.Geometry.SkeletonName);

                for (var i = 0; i < updatedGeometry.VertexCount(); i++)
                {
                    var inputVertexPosition = Vector3.Transform(
                        updatedGeometry.VertexArray[i].Position3(),
                        giveAnimationTo.ModelMatrix);
                    var result = weightTransfer.FindClosestWeights(inputVertexPosition);

                    updatedGeometry.VertexArray[i].BlendIndices = result.Bones;
                    updatedGeometry.VertexArray[i].BlendWeights = result.BlendWeights;
                }

                updatedGeometry.RebuildVertexBuffer();
                updatedGeometries.Add(updatedGeometry);
            }

            for (var index = 0; index < _giveAnimationToList.Count; index++)
                _giveAnimationToList[index].Geometry = updatedGeometries[index];
        }

        public void Undo()
        {
            for (var i = 0; i < _giveAnimationToList.Count; i++)
                _giveAnimationToList[i].Geometry = _originalGeometries[i];

            _selectionManager.SetState(_selectionOldState);
        }
    }
}
