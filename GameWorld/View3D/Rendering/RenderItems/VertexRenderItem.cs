using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    public class VertexRenderItem : IRenderItem
    {
        public VertexInstanceMesh VertexRenderer { get; set; }

        public Rmv2MeshNode Node { get; set; }
        public Matrix ModelMatrix { get; set; } = Matrix.Identity;
        public IReadOnlyList<Vector3>? WorldPositions { get; set; }
        public MeshPoseSnapshot? Pose { get; set; }
        public VertexSelectionState SelectedVertices { get; set; }
        private IReadOnlyList<Vector3>? _lastWorldPositions;
        private MeshObject? _lastAnimatedGeometry;
        private int _lastAnimatedVertexCount = -1;
        private VertexSelectionState? _lastSelectionState;
        private bool _lastUploadWasAnimated;
        private bool _needsUpload = true;

        public void MarkDirty()
        {
            _needsUpload = true;
        }

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (renderingTechnique != RenderingTechnique.Normal)
                return;

            if (Pose != null)
            {
                var geometry = Pose.Geometry;
                if (_needsUpload ||
                    !_lastUploadWasAnimated ||
                    !ReferenceEquals(
                        _lastAnimatedGeometry,
                        geometry) ||
                    _lastAnimatedVertexCount !=
                        geometry.VertexCount() ||
                    !ReferenceEquals(
                        _lastSelectionState,
                        SelectedVertices))
                {
                    VertexRenderer.UpdateAnimated(
                        geometry,
                        SelectedVertices);
                    _lastAnimatedGeometry = geometry;
                    _lastAnimatedVertexCount =
                        geometry.VertexCount();
                    _lastSelectionState =
                        SelectedVertices;
                    _lastUploadWasAnimated = true;
                    _needsUpload = false;
                }

                VertexRenderer.DrawAnimated(
                    Pose,
                    parameters.View,
                    parameters.Projection,
                    device);
                return;
            }

            if (WorldPositions != null)
            {
                if (_needsUpload ||
                    _lastUploadWasAnimated ||
                    !ReferenceEquals(
                        _lastWorldPositions,
                        WorldPositions) ||
                    !ReferenceEquals(
                        _lastSelectionState,
                        SelectedVertices))
                {
                    VertexRenderer.Update(
                        WorldPositions,
                        SelectedVertices);
                    _lastWorldPositions = WorldPositions;
                    _lastSelectionState =
                        SelectedVertices;
                    _lastUploadWasAnimated = false;
                    _needsUpload = false;
                }
            }
            else
            {
                VertexRenderer.Update(
                    Node.Geometry,
                    Node.RenderMatrix,
                    SelectedVertices);
            }

            VertexRenderer.Draw(
                parameters.View,
                parameters.Projection,
                device);
        }
    }
}
