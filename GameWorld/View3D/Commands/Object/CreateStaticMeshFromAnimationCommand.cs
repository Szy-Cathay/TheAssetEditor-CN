using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Shared.Core.Services;

namespace GameWorld.Core.Commands.Object
{
    public class CreateStaticMeshFromAnimationCommand : IRedoableCommand
    {
        ISceneNode _parent;
        List<Rmv2MeshNode> _sourceMeshes;
        AnimationFrame _frame;
        GroupNode _createdGroup;

        public string HintText => LocalizationManager.Instance?.Get("Kitbash.CommandHint.CreateStaticMeshFromAnimation")
            ?? "从动画创建静态网格";
        public bool IsMutation => true;

        public void Configure(ISceneNode parent, List<Rmv2MeshNode> sourceMeshes, AnimationFrame frame)
        {
            _parent = parent;
            _sourceMeshes = new List<Rmv2MeshNode>(sourceMeshes);
            _frame = frame;
        }

        public void Execute()
        {
            if (_sourceMeshes.Count == 0)
                throw new InvalidOperationException("At least one mesh is required.");

            _createdGroup = new GroupNode("staticMesh");
            var clonedMeshes = new List<Rmv2MeshNode>(_sourceMeshes.Count);
            foreach (var sourceMesh in _sourceMeshes)
            {
                var clone = SceneNodeHelper.CloneNode(sourceMesh);
                _createdGroup.AddObject(clone);
                clonedMeshes.Add(clone);
            }

            CreateAnimatedMeshPoseCommand.ApplyFrame(clonedMeshes, _frame, true);
            _parent.AddObject(_createdGroup);
        }

        public void Undo()
        {
            _parent.RemoveObject(_createdGroup);
        }

        public void Redo()
        {
            _parent.AddObject(_createdGroup);
        }
    }
}
