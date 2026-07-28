using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace GameWorld.Core.Rendering
{
    /// <summary>
    /// Instance data for vertex point rendering.
    /// Each instance is a camera-facing quad rendered as a circular point.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexPointInstanceData : IVertexType
    {
        public Vector3 InstancePosition;   // World position of the vertex
        public float InstanceScale;        // Point diameter in pixels
        public Vector3 InstanceColor;      // RGB color (lerped between selected/deselected)
        public float InstanceWeight;       // Selection weight (0.0 = unselected, 1.0 = selected)

        public static readonly VertexDeclaration VertexDeclaration;
        static VertexPointInstanceData()
        {
            var elements = new VertexElement[]
            {
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 2),
                new VertexElement(sizeof(float) * 3, VertexElementFormat.Single, VertexElementUsage.Normal, 1),
                new VertexElement(sizeof(float) * 4, VertexElementFormat.Vector3, VertexElementUsage.Normal, 2),
                new VertexElement(sizeof(float) * 7, VertexElementFormat.Single, VertexElementUsage.Normal, 3),
            };
            VertexDeclaration = new VertexDeclaration(elements);
        }

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }

    /// <summary>
    /// Renders edit mode vertices as camera-facing circular points with screen-space size.
    /// Based on Blender's overlay_edit_mesh_vert.glsl approach.
    /// </summary>
    public class VertexInstanceMesh : IDisposable
    {
        Effect _effect;
        VertexDeclaration _instanceVertexDeclaration;
        VertexDeclaration _quadVertexDeclaration;
        GraphicsDevice _device;

        DynamicVertexBuffer _instanceBuffer;
        VertexBuffer _geometryBuffer;
        IndexBuffer _indexBuffer;

        VertexBufferBinding[] _staticBindings;
        VertexBufferBinding[] _animatedBindings;
        VertexPointInstanceData[] _instanceData;

        const int InitialInstanceCapacity = 50000;
        int _currentInstanceCount;
        internal int InstanceUploadCount { get; private set; }

        // Colors - EXACT Blender match: unselected = black (visible via z-bias), selected = orange
        // Blender theme: TH_VERTEX = 0x000000ff (black), TH_VERTEX_SELECT = 0xff7a00ff (orange)
        Vector3 _selectedColour = new(1.0f, 0.47f, 0.0f);           // Orange (255, 122, 0)
        Vector3 _deselectedColour = new(0.0f, 0.0f, 0.0f);          // Black (0, 0, 0)

        // Screen-space vertex size in pixels (diameter) - EXACT Blender match
        // Blender: sizes.vert = max(1.0, TH_VERTEX_SIZE * sqrt2 / 2) = ~2.12, then * 2.0 = 4.24 pixels
        public float VertexPixelSize { get; set; } = 5.5f;

        // Additional size boost for selected vertices (pixels added to diameter)
        // Blender uses same base size, but we add slight boost for visibility
        public float SelectedSizeBoost { get; set; } = 2.0f;

        // Selection threshold multiplier (selection radius = render radius * this)
        public float SelectionThresholdMultiplier { get; set; } = 2.0f;

        public VertexInstanceMesh(IDeviceResolver deviceResolverComponent, IScopedResourceLibrary resourceLibrary)
        {
            Initialize(deviceResolverComponent.Device, resourceLibrary);
        }

        void Initialize(GraphicsDevice device, IScopedResourceLibrary resourceLib)
        {
            _device = device;
            _effect = resourceLib.GetStaticEffect(ShaderTypes.VertexPoint);

            _instanceVertexDeclaration = VertexPointInstanceData.VertexDeclaration;
            GenerateGeometry(device);
            _instanceBuffer = new DynamicVertexBuffer(
                device,
                _instanceVertexDeclaration,
                InitialInstanceCapacity,
                BufferUsage.WriteOnly);
            _instanceData =
                new VertexPointInstanceData[
                    InitialInstanceCapacity];

            _staticBindings = new VertexBufferBinding[2];
            _animatedBindings = new VertexBufferBinding[3];
            UpdateInstanceBindings();
        }

        /// <summary>
        /// Generate a unit quad [-0.5, 0.5] for billboard rendering.
        /// The shader will clip this to a circle.
        /// </summary>
        void GenerateGeometry(GraphicsDevice device)
        {
            _quadVertexDeclaration = new VertexDeclaration(
                new VertexElement(
                    0,
                    VertexElementFormat.Vector3,
                    VertexElementUsage.Position,
                    1),
                new VertexElement(
                    sizeof(float) * 3,
                    VertexElementFormat.Vector2,
                    VertexElementUsage.TextureCoordinate,
                    2));

            // Unit quad centered at origin, with UV coordinates for circle clipping
            var vertices = new VertexPositionTexture[4];
            vertices[0] = new VertexPositionTexture(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1));  // Bottom-left
            vertices[1] = new VertexPositionTexture(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1));   // Bottom-right
            vertices[2] = new VertexPositionTexture(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0));   // Top-left
            vertices[3] = new VertexPositionTexture(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0));    // Top-right

            _geometryBuffer = new VertexBuffer(
                device,
                _quadVertexDeclaration,
                4,
                BufferUsage.WriteOnly);
            _geometryBuffer.SetData(vertices);

            // Two triangles forming a quad
            var indices = new int[6];
            indices[0] = 0; indices[1] = 1; indices[2] = 2;  // First triangle
            indices[3] = 1; indices[4] = 3; indices[5] = 2;  // Second triangle

            _indexBuffer = new IndexBuffer(device, typeof(int), 6, BufferUsage.WriteOnly);
            _indexBuffer.SetData(indices);
        }

        /// <summary>
        /// Update instance data for all vertices.
        /// Point size is converted from pixels to clip space by the shader.
        /// </summary>
        /// <param name="geo">Mesh geometry</param>
        /// <param name="modelMatrix">Model world matrix</param>
        /// <param name="selectedVertexes">Vertex selection state with weights</param>
        public void Update(
            MeshObject geo,
            Matrix modelMatrix,
            VertexSelectionState selectedVertexes)
        {
            _currentInstanceCount = geo.VertexCount();
            EnsureInstanceCapacity(_currentInstanceCount);

            for (var i = 0; i < _currentInstanceCount; i++)
            {
                // World position of the vertex
                var vertPos = Vector3.Transform(geo.GetVertexById(i), modelMatrix);

                // Color based on selection weight
                var weight = selectedVertexes.VertexWeights[i];
                var color = Vector3.Lerp(_deselectedColour, _selectedColour, weight);

                _instanceData[i].InstancePosition = vertPos;
                _instanceData[i].InstanceScale =
                    VertexPixelSize +
                    weight * SelectedSizeBoost;
                _instanceData[i].InstanceColor = color;
                _instanceData[i].InstanceWeight = weight;
            }

            UploadInstances();
        }

        public void Update(
            IReadOnlyList<Vector3> worldPositions,
            VertexSelectionState selectedVertexes)
        {
            _currentInstanceCount =
                worldPositions.Count;
            EnsureInstanceCapacity(_currentInstanceCount);

            for (var i = 0; i < _currentInstanceCount; i++)
            {
                var vertPos = worldPositions[i];
                var weight = selectedVertexes.VertexWeights[i];
                var color = Vector3.Lerp(
                    _deselectedColour,
                    _selectedColour,
                    weight);

                _instanceData[i].InstancePosition = vertPos;
                _instanceData[i].InstanceScale =
                    VertexPixelSize +
                    weight * SelectedSizeBoost;
                _instanceData[i].InstanceColor = color;
                _instanceData[i].InstanceWeight = weight;
            }

            UploadInstances();
        }

        public void UpdateAnimated(
            MeshObject geometry,
            VertexSelectionState selectedVertexes)
        {
            _currentInstanceCount = geometry.VertexCount();
            EnsureInstanceCapacity(_currentInstanceCount);

            for (var i = 0; i < _currentInstanceCount; i++)
            {
                var weight =
                    selectedVertexes.VertexWeights[i];
                _instanceData[i].InstanceScale =
                    VertexPixelSize +
                    weight * SelectedSizeBoost;
                _instanceData[i].InstanceColor =
                    Vector3.Lerp(
                        _deselectedColour,
                        _selectedColour,
                        weight);
                _instanceData[i].InstanceWeight = weight;
            }

            UploadInstances();
        }

        /// <summary>
        /// Calculate the world-space selection threshold for a vertex.
        /// Used by IntersectionMath for ray-vertex hit testing.
        /// </summary>
        public float GetSelectionThresholdWorld(float distanceToCamera, float cameraFov, float viewportHeight)
        {
            float fovScale = 2.0f * MathF.Tan(cameraFov / 2.0f) / viewportHeight;
            // Selection radius = render radius * multiplier
            return (VertexPixelSize * 0.5f * SelectionThresholdMultiplier) * distanceToCamera * fovScale;
        }

        public void Draw(
            Matrix view,
            Matrix projection,
            GraphicsDevice device)
        {
            if (_currentInstanceCount == 0)
                return;

            _effect.CurrentTechnique = _effect.Techniques["VertexPoint"];
            _effect.Parameters["ViewProjection"].SetValue(view * projection);
            _effect.Parameters["ViewportSize"].SetValue(
                new Vector2(
                    device.Viewport.Width,
                    device.Viewport.Height));

            // Alpha blending required for anti-aliased circle edges and outline ring transparency
            device.BlendState = BlendState.AlphaBlend;

            DrawInstances(device, _staticBindings);

            device.BlendState = BlendState.Opaque;
        }

        public void DrawAnimated(
            MeshPoseSnapshot pose,
            Matrix view,
            Matrix projection,
            GraphicsDevice device)
        {
            if (_currentInstanceCount == 0)
                return;

            _effect.CurrentTechnique =
                _effect.Techniques["AnimatedVertexPoint"];
            _effect.Parameters["World"].SetValue(
                pose.WorldTransform);
            _effect.Parameters["ViewProjection"].SetValue(
                view * projection);
            _effect.Parameters["ViewportSize"].SetValue(
                new Vector2(
                    device.Viewport.Width,
                    device.Viewport.Height));
            _effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(pose.ApplyAnimation);
            _effect.Parameters["Animation_WeightCount"]
                .SetValue(pose.AnimationWeightCount);
            if (pose.ApplyAnimation)
            {
                _effect.Parameters["Animation_Tranforms"]
                    .SetValue(pose.AnimationTransforms);
            }

            _animatedBindings[1] =
                new VertexBufferBinding(
                    pose.Geometry
                        .GetGeometryContext()
                        .VertexBuffer,
                    0,
                    1);
            DrawInstances(device, _animatedBindings);

            device.BlendState = BlendState.Opaque;
        }

        void DrawInstances(
            GraphicsDevice device,
            VertexBufferBinding[] bindings)
        {
            device.BlendState = BlendState.AlphaBlend;
            device.Indices = _indexBuffer;
            _effect.CurrentTechnique.Passes[0].Apply();
            device.SetVertexBuffers(bindings);
            device.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                4,
                0,
                2,
                _currentInstanceCount);
        }

        void EnsureInstanceCapacity(int requiredCount)
        {
            if (requiredCount <= _instanceData.Length)
                return;

            var newCapacity = Math.Max(
                requiredCount,
                _instanceData.Length * 2);
            Array.Resize(
                ref _instanceData,
                newCapacity);
            _instanceBuffer.Dispose();
            _instanceBuffer =
                new DynamicVertexBuffer(
                    _device,
                    _instanceVertexDeclaration,
                    newCapacity,
                    BufferUsage.WriteOnly);
            UpdateInstanceBindings();
        }

        void UpdateInstanceBindings()
        {
            _staticBindings[0] =
                new VertexBufferBinding(_geometryBuffer);
            _staticBindings[1] =
                new VertexBufferBinding(
                    _instanceBuffer,
                    0,
                    1);
            _animatedBindings[0] =
                new VertexBufferBinding(_geometryBuffer);
            _animatedBindings[2] =
                new VertexBufferBinding(
                    _instanceBuffer,
                    0,
                    1);
        }

        void UploadInstances()
        {
            if (_currentInstanceCount == 0)
                return;

            _instanceBuffer.SetData(
                _instanceData,
                0,
                _currentInstanceCount,
                SetDataOptions.Discard);
            InstanceUploadCount++;
        }

        public void Dispose()
        {
            _instanceVertexDeclaration?.Dispose();
            _quadVertexDeclaration?.Dispose();
            _instanceBuffer?.Dispose();
            _geometryBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }
    }
}
