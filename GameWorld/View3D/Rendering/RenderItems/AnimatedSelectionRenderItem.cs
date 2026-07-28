using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    public sealed class AnimatedSelectionRenderItem : IRenderItem
    {
        MeshPoseSnapshot _pose;
        readonly IScopedResourceLibrary _resourceLibrary;
        readonly Vector4 _colour;
        IReadOnlyList<int>? _selectedFaces;

        public AnimatedSelectionRenderItem(
            MeshPoseSnapshot pose,
            IScopedResourceLibrary resourceLibrary,
            Vector4 colour,
            IReadOnlyList<int>? selectedFaces = null)
        {
            _pose = pose;
            _resourceLibrary = resourceLibrary;
            _colour = colour;
            _selectedFaces = selectedFaces;
        }

        public void UpdatePose(MeshPoseSnapshot pose)
        {
            _pose = pose;
        }

        public void UpdateSelectedFaces(
            IReadOnlyList<int>? selectedFaces)
        {
            _selectedFaces = selectedFaces;
        }

        public bool SupportsTechnique(
            RenderingTechnique technique)
        {
            return technique == RenderingTechnique.Normal;
        }

        public void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            if (!SupportsTechnique(renderingTechnique))
                return;

            var effect = _resourceLibrary.GetStaticEffect(
                ShaderTypes.AnimatedSelection);
            effect.CurrentTechnique =
                effect.Techniques[
                    _pose.ApplyAnimation
                        ? "AnimatedSelection"
                        : "StaticSelection"];
            effect.Parameters["World"].SetValue(
                _pose.WorldTransform);
            effect.Parameters["View"].SetValue(parameters.View);
            effect.Parameters["Projection"].SetValue(
                parameters.Projection);
            effect.Parameters["SelectionColour"].SetValue(
                _colour);
            effect.Parameters["SelectionDepthBias"].SetValue(
                0.00001f);
            effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(_pose.ApplyAnimation);
            effect.Parameters["Animation_WeightCount"]
                .SetValue(_pose.AnimationWeightCount);
            if (_pose.ApplyAnimation)
            {
                effect.Parameters["Animation_Tranforms"]
                    .SetValue(_pose.AnimationTransforms);
            }
            effect.CurrentTechnique.Passes[0].Apply();

            var geometry = _pose.Geometry;
            var graphicsGeometry =
                geometry.GetGeometryContext();
            device.Indices = graphicsGeometry.IndexBuffer;
            device.SetVertexBuffer(
                graphicsGeometry.VertexBuffer);
            if (_selectedFaces == null)
            {
                device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    0,
                    0,
                    geometry.GetIndexCount() / 3);
                return;
            }

            DrawSelectedFaces(device);
        }

        void DrawSelectedFaces(GraphicsDevice device)
        {
            if (_selectedFaces.Count == 0)
                return;

            var batchStart = _selectedFaces[0];
            var batchCount = 1;
            for (var i = 1; i < _selectedFaces.Count; i++)
            {
                if (_selectedFaces[i] ==
                    _selectedFaces[i - 1] + 3)
                {
                    batchCount++;
                    continue;
                }

                device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    0,
                    batchStart,
                    batchCount);
                batchStart = _selectedFaces[i];
                batchCount = 1;
            }

            device.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                0,
                batchStart,
                batchCount);
        }
    }
}
