using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components.Selection
{
    public sealed class BoneSelectionHighlightComponent : BaseComponent
    {
        private readonly SelectionManager _selectionManager;
        private readonly RenderEngineComponent _renderEngine;

        public BoneSelectionHighlightComponent(
            SelectionManager selectionManager,
            RenderEngineComponent renderEngine)
        {
            _selectionManager = selectionManager;
            _renderEngine = renderEngine;
        }

        public override void Draw(GameTime gameTime)
        {
            if (_selectionManager.GetState() is not
                    BoneSelectionState selection ||
                selection.RenderObject is not Rmv2MeshNode mesh ||
                selection.Skeleton == null)
            {
                return;
            }

            var frame = mesh.AnimationPlayer
                ?.GetCurrentAnimationFrame();
            if (frame == null)
                return;

            foreach (var boneIndex in selection.SelectedBones)
            {
                var bone = frame.GetSkeletonAnimatedWorld(
                    selection.Skeleton,
                    boneIndex);
                _renderEngine.AddRenderLines(
                    LineHelper.CreateCube(
                        Matrix.CreateScale(0.06f) *
                        bone *
                        mesh.RenderMatrix,
                        Color.Red));
            }
        }
    }
}
