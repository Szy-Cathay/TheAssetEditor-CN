using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;
using System.Collections.Generic;

namespace GameWorld.Core.Commands.Object
{
    public class CreateAnimatedMeshPoseCommand : IRedoableCommand
    {
        List<MeshObject> _originalGeometries;
        List<MeshObject> _posedGeometries;

        List<Rmv2MeshNode> _meshNodes;
        AnimationFrame _frame;
        bool _convertToStaticFrame;

        public string HintText { get => "Created static mesh from animation"; }
        public bool IsMutation { get => true; }

        public void Configure(List<Rmv2MeshNode> meshNodes, AnimationFrame frame, bool convertToStaticFrame = false)
        {
            _meshNodes = new List<Rmv2MeshNode>(meshNodes);
            _frame = frame;
            _convertToStaticFrame = convertToStaticFrame;
        }


        public void Execute()
        {
            _originalGeometries = _meshNodes.Select(x => x.Geometry).ToList();
            _posedGeometries = _originalGeometries.Select(x => x.Clone()).ToList();
            for (var i = 0; i < _meshNodes.Count; i++)
                ApplyFrame(_meshNodes[i], _posedGeometries[i], _frame, _convertToStaticFrame);
            Redo();
        }

        public void Redo()
        {
            for (var i = 0; i < _meshNodes.Count; i++)
                _meshNodes[i].Geometry = _posedGeometries[i];
        }

        internal static void ApplyFrame(IEnumerable<Rmv2MeshNode> meshNodes, AnimationFrame frame, bool convertToStaticFrame)
        {
            foreach (var node in meshNodes)
                ApplyFrame(node, node.Geometry, frame, convertToStaticFrame);
        }

        static void ApplyFrame(Rmv2MeshNode node, MeshObject geometry, AnimationFrame frame, bool convertToStaticFrame)
        {
            if (geometry.WeightCount == 0)
                return;
            var meshHelper = new MeshAnimationHelper(node, Matrix.Identity);

            for (var i = 0; i < geometry.VertexCount(); i++)
            {
                var vert = meshHelper.GetVertexTransform(frame, i);
                geometry.TransformVertex(i, vert);
            }

            if (convertToStaticFrame)
            {
                geometry.ChangeVertexType(UiVertexFormat.Static);
                geometry.UpdateSkeletonName(string.Empty);
            }

            geometry.RebuildVertexBuffer();
        }

        public void Undo()
        {
            for (var i = 0; i < _meshNodes.Count; i++)
                _meshNodes[i].Geometry = _originalGeometries[i];
        }
    }
}
