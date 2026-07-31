using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Animation
{
    public sealed class MeshPoseSnapshot
    {
        internal const int MaxAnimationTransformCount = 256;

        public MeshObject Geometry { get; private set; }
        public Matrix WorldTransform { get; private set; }
        public Matrix[] AnimationTransforms { get; private set; }
        public int AnimationWeightCount { get; private set; }
        public bool ApplyAnimation { get; private set; }

        MeshPoseSnapshot(
            MeshObject geometry,
            Matrix worldTransform,
            Matrix[] animationTransforms,
            int animationWeightCount,
            bool applyAnimation)
        {
            Geometry = geometry;
            WorldTransform = worldTransform;
            AnimationTransforms = animationTransforms;
            AnimationWeightCount = animationWeightCount;
            ApplyAnimation = applyAnimation;
        }

        internal void UpdateForRendering(
            MeshObject geometry,
            Matrix worldTransform,
            Matrix[] animationTransforms,
            bool applyAnimation)
        {
            Geometry = geometry;
            WorldTransform = worldTransform;
            AnimationTransforms = animationTransforms;
            AnimationWeightCount = applyAnimation
                ? geometry.WeightCount
                : 0;
            ApplyAnimation = applyAnimation;
        }

        public static MeshPoseSnapshot Capture(
            Rmv2MeshNode meshNode)
        {
            ArgumentNullException.ThrowIfNull(meshNode);

            var animationPlayer = meshNode.AnimationPlayer;
            var currentFrame =
                animationPlayer?.GetCurrentAnimationFrame();
            var applyAnimation =
                ShouldApplyAnimation(
                    meshNode,
                    currentFrame);
            var transforms = !applyAnimation
                ? Array.Empty<Matrix>()
                : currentFrame!.BoneTransforms
                    .Select(transform => transform.WorldTransform)
                    .ToArray();

            return Create(
                meshNode.Geometry,
                meshNode.GetRenderWorldMatrix(),
                transforms,
                applyAnimation);
        }

        internal static bool ShouldApplyAnimation(
            Rmv2MeshNode meshNode,
            AnimationFrame? currentFrame)
        {
            return
                meshNode.AnimationPlayer?.IsEnabled == true &&
                currentFrame != null &&
                meshNode.Geometry.VertexFormat is
                    UiVertexFormat.Weighted or
                    UiVertexFormat.Cinematic;
        }

        internal static MeshPoseSnapshot Create(
            MeshObject geometry,
            Matrix worldTransform,
            IReadOnlyList<Matrix> animationTransforms,
            bool applyAnimation)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            ArgumentNullException.ThrowIfNull(animationTransforms);

            var transformBuffer =
                new Matrix[MaxAnimationTransformCount];
            Array.Fill(transformBuffer, Matrix.Identity);
            var copyCount = Math.Min(
                animationTransforms.Count,
                transformBuffer.Length);
            for (var i = 0; i < copyCount; i++)
                transformBuffer[i] = animationTransforms[i];

            var weightCount = applyAnimation
                ? geometry.WeightCount
                : 0;
            return new MeshPoseSnapshot(
                geometry,
                worldTransform,
                transformBuffer,
                weightCount,
                applyAnimation);
        }

        internal static MeshPoseSnapshot CreateForRendering(
            MeshObject geometry,
            Matrix worldTransform,
            Matrix[] animationTransforms,
            bool applyAnimation)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            ArgumentNullException.ThrowIfNull(
                animationTransforms);
            if (animationTransforms.Length !=
                MaxAnimationTransformCount)
            {
                throw new ArgumentException(
                    $"Render animation buffers must contain {MaxAnimationTransformCount} transforms.",
                    nameof(animationTransforms));
            }

            return new MeshPoseSnapshot(
                geometry,
                worldTransform,
                animationTransforms,
                applyAnimation
                    ? geometry.WeightCount
                    : 0,
                applyAnimation);
        }

        public Matrix GetVertexToWorldTransform(
            int vertexIndex)
        {
            return
                GetSkinTransform(vertexIndex) *
                WorldTransform;
        }

        public Vector3 GetWorldPosition(int vertexIndex)
        {
            return Vector3.Transform(
                Geometry.GetVertexById(vertexIndex),
                GetVertexToWorldTransform(vertexIndex));
        }

        public Vector3[] GetWorldPositions()
        {
            var positions =
                new Vector3[Geometry.VertexCount()];
            FillWorldPositions(positions);
            return positions;
        }

        public BoundingBox GetConservativeAnimatedBounds()
        {
            var bounds = Geometry.BoundingBox;
            if (!ApplyAnimation)
                return bounds;

            var min = bounds.Min;
            var max = bounds.Max;
            for (var transformIndex = 0;
                 transformIndex < AnimationTransforms.Length;
                 transformIndex++)
            {
                var transform =
                    AnimationTransforms[transformIndex];
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0
                            ? bounds.Min.X
                            : bounds.Max.X,
                        (corner & 2) == 0
                            ? bounds.Min.Y
                            : bounds.Max.Y,
                        (corner & 4) == 0
                            ? bounds.Min.Z
                            : bounds.Max.Z);
                    var transformed = Vector3.Transform(
                        point,
                        transform);
                    min = Vector3.Min(min, transformed);
                    max = Vector3.Max(max, transformed);
                }
            }

            return new BoundingBox(min, max);
        }

        public void FillWorldPositions(
            Vector3[] positions)
        {
            ArgumentNullException.ThrowIfNull(positions);
            if (positions.Length != Geometry.VertexCount())
            {
                throw new ArgumentException(
                    "The position buffer must match the geometry vertex count.",
                    nameof(positions));
            }

            for (var vertexIndex = 0;
                 vertexIndex < positions.Length;
                 vertexIndex++)
            {
                positions[vertexIndex] =
                    GetWorldPosition(vertexIndex);
            }
        }

        Matrix GetSkinTransform(int vertexIndex)
        {
            if (!ApplyAnimation)
                return Matrix.Identity;

            var vertex = Geometry.GetVertexExtented(vertexIndex);
            var transform = new Matrix();
            for (var weightIndex = 0;
                 weightIndex < AnimationWeightCount;
                 weightIndex++)
            {
                var boneIndex = (int)GetComponent(
                    vertex.BlendIndices,
                    weightIndex);
                var weight = GetComponent(
                    vertex.BlendWeights,
                    weightIndex);
                var boneTransform =
                    boneIndex >= 0 &&
                    boneIndex < AnimationTransforms.Length
                        ? AnimationTransforms[boneIndex]
                        : Matrix.Identity;
                transform += boneTransform * weight;
            }

            return transform;
        }

        static float GetComponent(Vector4 value, int index)
        {
            return index switch
            {
                0 => value.X,
                1 => value.Y,
                2 => value.Z,
                3 => value.W,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(index))
            };
        }
    }

    internal sealed class MeshPoseRenderCache
    {
        static readonly Matrix[] IdentityTransforms =
            CreateIdentityBuffer();

        readonly Dictionary<AnimationPlayer, TransformBufferEntry>
            _transformBuffers = [];
        readonly Dictionary<Rmv2MeshNode, MeshPoseSnapshot>
            _poses = [];

        public MeshPoseSnapshot Capture(
            Rmv2MeshNode meshNode)
        {
            ArgumentNullException.ThrowIfNull(meshNode);

            var animationPlayer = meshNode.AnimationPlayer;
            var currentFrame =
                animationPlayer?.GetCurrentAnimationFrame();
            var applyAnimation =
                MeshPoseSnapshot.ShouldApplyAnimation(
                    meshNode,
                    currentFrame);
            var transforms =
                applyAnimation &&
                animationPlayer != null &&
                currentFrame != null
                    ? GetTransforms(
                        animationPlayer,
                        currentFrame)
                    : IdentityTransforms;

            var worldTransform =
                meshNode.GetRenderWorldMatrix();
            if (!_poses.TryGetValue(
                    meshNode,
                    out var pose))
            {
                pose =
                    MeshPoseSnapshot.CreateForRendering(
                        meshNode.Geometry,
                        worldTransform,
                        transforms,
                        applyAnimation);
                _poses.Add(meshNode, pose);
            }
            else
            {
                pose.UpdateForRendering(
                    meshNode.Geometry,
                    worldTransform,
                    transforms,
                    applyAnimation);
            }

            return pose;
        }

        public void Clear()
        {
            _poses.Clear();
            _transformBuffers.Clear();
        }

        Matrix[] GetTransforms(
            AnimationPlayer animationPlayer,
            AnimationFrame currentFrame)
        {
            if (!_transformBuffers.TryGetValue(
                    animationPlayer,
                    out var entry))
            {
                entry = new TransformBufferEntry();
                _transformBuffers.Add(
                    animationPlayer,
                    entry);
            }

            if (ReferenceEquals(
                    entry.Frame,
                    currentFrame))
            {
                return entry.Transforms;
            }

            var copyCount = Math.Min(
                currentFrame.BoneTransforms.Count,
                entry.Transforms.Length);
            for (var i = 0; i < copyCount; i++)
            {
                entry.Transforms[i] =
                    currentFrame.BoneTransforms[i]
                        .WorldTransform;
            }

            for (var i = copyCount;
                 i < entry.TransformCount;
                 i++)
            {
                entry.Transforms[i] = Matrix.Identity;
            }

            entry.Frame = currentFrame;
            entry.TransformCount = copyCount;
            return entry.Transforms;
        }

        static Matrix[] CreateIdentityBuffer()
        {
            var transforms =
                new Matrix[
                    MeshPoseSnapshot
                        .MaxAnimationTransformCount];
            Array.Fill(transforms, Matrix.Identity);
            return transforms;
        }

        sealed class TransformBufferEntry
        {
            public AnimationFrame? Frame { get; set; }
            public Matrix[] Transforms { get; } =
                CreateIdentityBuffer();
            public int TransformCount { get; set; }
        }
    }
}
