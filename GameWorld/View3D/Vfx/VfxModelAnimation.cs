using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Vertex;

namespace GameWorld.Core.Vfx;

internal sealed class VfxModelAnimationBudget
{
    private long _vertices, _indices;

    public void Reserve(long vertices, long indices)
    {
        if (_vertices + vertices > 1000000 || _indices + indices > 6000000)
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.RangeUnsupported");
        _vertices += vertices;
        _indices += indices;
    }

    public void Release(long vertices, long indices) { _vertices -= vertices; _indices -= indices; }

    public (long Vertices, long Indices) Capture() => (_vertices, _indices);

    public void Restore((long Vertices, long Indices) checkpoint) => (_vertices, _indices) = checkpoint;
}

// VFX controls play the clip in particle-life phase, independently of its file duration.
internal static class VfxModelAnimation
{
    public static VfxPreviewModel BakeRigid(EffekseerVertex[] vertices, int[] indices, string skeletonName, byte[] animationBytes,
        int matrixIndex, Vector3 pivot, float cycles, float maximumLife, VfxModelAnimationBudget budget)
    {
        try
        {
            var file = ReadAnimation(animationBytes);
            if (skeletonName != "building" || file.Header.SkeletonName != skeletonName || file.Bones.Length is < 1 or > 256)
                throw new VfxMappingException("RigidData/Skeleton", "Vfx.Preview.GeometryUnsupported");
            if (matrixIndex < 0 || matrixIndex >= file.Bones.Length)
                throw new VfxMappingException("RigidData/MatrixIndex", "Vfx.Preview.GeometryUnsupported");
            if (file.Bones.Any(bone => bone.ParentId != -1))
                throw new VfxMappingException("RigidData/Hierarchy", "Vfx.Preview.GeometryUnsupported");
            if (!float.IsFinite(cycles) || cycles < 0 || cycles > 10 || !float.IsFinite(maximumLife) || maximumLife <= 0)
                throw new VfxMappingException("AnimSpeed", "Vfx.Preview.RangeUnsupported");
            if (!float.IsFinite(file.Header.FrameRate) || file.Header.FrameRate <= 0
                || !float.IsFinite(file.Header.AnimationTotalPlayTimeInSec) || file.Header.AnimationTotalPlayTimeInSec <= 0)
                throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
            var keys = new List<(Vector3 Position, Quaternion Rotation)>();
            foreach (var part in file.AnimationParts)
            {
                if (part.TranslationMappings.Count != file.Bones.Length || part.RotationMappings.Count != file.Bones.Length)
                    throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
                var translation = part.TranslationMappings[matrixIndex]; var rotation = part.RotationMappings[matrixIndex];
                IEnumerable<AnimationFile.Frame> sourceFrames = part.DynamicFrames.Count == 0 ? [part.StaticFrame] : part.DynamicFrames;
                foreach (var frame in sourceFrames)
                {
                    var position = Vector3.Zero; var quaternion = Quaternion.Identity;
                    if (translation.HasValue)
                    {
                        var value = (translation.IsStatic ? part.StaticFrame : frame).Transforms[translation.Id];
                        position = new(value.X, value.Y, value.Z);
                    }
                    if (rotation.HasValue)
                    {
                        var value = (rotation.IsStatic ? part.StaticFrame : frame).Quaternion[rotation.Id];
                        quaternion = new(value.X, value.Y, value.Z, value.W);
                    }
                    if (!Finite(position) || !float.IsFinite(quaternion.LengthSquared()) || quaternion.LengthSquared() < .000001f)
                        throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
                    keys.Add((position, Quaternion.Normalize(quaternion)));
                }
            }
            if (keys.Count == 0 || !Finite(pivot)) throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
            var requestedFrames = cycles == 0 ? 1 : Math.Max(2, Math.Ceiling((double)maximumLife * 60 / cycles));
            if (!double.IsFinite(requestedFrames) || requestedFrames > 1800)
                throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.RangeUnsupported");
            var frames = (int)requestedFrames; var vertexCount = (long)vertices.Length * frames; var indexCount = (long)indices.Length * frames;
            budget.Reserve(vertexCount, indexCount);
            try
            {
                var baked = new EffekseerVertex[checked((int)vertexCount)];
                for (var frame = 0; frame < frames; ++frame)
                {
                    var sample = Math.Min(keys.Count - 1, frame / (double)frames * file.Header.FrameRate * file.Header.AnimationTotalPlayTimeInSec);
                    var first = (int)Math.Floor(sample); var second = Math.Min(first + 1, keys.Count - 1); var fraction = (float)(sample - first);
                    var transform = Matrix.CreateFromQuaternion(Quaternion.Slerp(keys[first].Rotation, keys[second].Rotation, fraction))
                        * Matrix.CreateTranslation(Vector3.Lerp(keys[first].Position, keys[second].Position, fraction));
                    for (var index = 0; index < vertices.Length; ++index)
                    {
                        var vertex = vertices[index];
                        vertex.Position = Vector3.Transform(vertex.Position + pivot, transform);
                        vertex.Normal = Unit(Vector3.TransformNormal(vertex.Normal, transform));
                        vertex.Tangent = Unit(Vector3.TransformNormal(vertex.Tangent, transform));
                        vertex.Binormal = Unit(Vector3.TransformNormal(vertex.Binormal, transform));
                        if (!Finite(vertex.Position)) throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
                        baked[frame * vertices.Length + index] = vertex;
                    }
                }
                return new(baked.AsSpan(0, vertices.Length).ToArray(), indices, frames == 1 ? null : baked, frames, cycles, RigidAnimation: true);
            }
            catch { budget.Release(vertexCount, indexCount); throw; }
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or System.IO.EndOfStreamException
            or System.IO.InvalidDataException or FormatException or OverflowException or NullReferenceException)
        { throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData"); }
    }

    public static VfxPreviewModel Bake(CommonVertex[] source, EffekseerVertex[] vertices, int[] indices,
        string skeletonName, byte[] skeletonBytes, byte[] animationBytes, float cycles, float maximumLife, VfxModelAnimationBudget budget)
    {
        try
        {
            return BakeCore(source, vertices, indices, skeletonName, skeletonBytes, animationBytes, cycles, maximumLife, budget);
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or System.IO.EndOfStreamException
            or System.IO.InvalidDataException or FormatException or OverflowException)
        {
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
        }
    }

    private static VfxPreviewModel BakeCore(CommonVertex[] source, EffekseerVertex[] vertices, int[] indices,
        string skeletonName, byte[] skeletonBytes, byte[] animationBytes, float cycles, float maximumLife, VfxModelAnimationBudget budget)
    {
        if (!float.IsFinite(cycles) || cycles < 0 || cycles > 10 || !float.IsFinite(maximumLife) || maximumLife <= 0)
            throw new VfxMappingException("AnimSpeed", "Vfx.Preview.RangeUnsupported");
        var skeletonFile = ReadAnimation(skeletonBytes);
        var animationFile = ReadAnimation(animationBytes);
        if (skeletonFile.Bones.Length is < 1 or > 256 || !string.Equals(skeletonFile.Header.SkeletonName, skeletonName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(animationFile.Header.SkeletonName, skeletonName, StringComparison.OrdinalIgnoreCase))
            throw new VfxMappingException("RigidData/Skeleton", "Vfx.Preview.GeometryUnsupported");
        var skeleton = GameSkeleton.CreateFromAnimationFile(skeletonFile, new AnimationPlayer());
        for (var bone = 0; bone < skeleton.BoneCount; ++bone)
            if (skeleton.GetParentBoneIndex(bone) >= bone)
                throw new VfxMappingException("RigidData/Skeleton", "Vfx.Preview.GeometryUnsupported");
        if (animationFile.Bones.Any(bone => skeleton.GetBoneIndexByName(bone.Name) < 0))
            throw new VfxMappingException("RigidData/Skeleton", "Vfx.Preview.GeometryUnsupported");
        var clip = new AnimationClip(animationFile, skeleton);
        if (clip.DynamicFrames.Count == 0 || clip.Duration <= TimeSpan.Zero
            || !float.IsFinite(animationFile.Header.FrameRate) || animationFile.Header.FrameRate <= 0)
            throw new VfxMappingException("AnimFile", "Vfx.Preview.InvalidData");
        var skinWeights = new float[source.Length][];
        for (var index = 0; index < source.Length; ++index)
        {
            var vertex = source[index];
            if (vertex.WeightCount is not (2 or 4) || vertex.BoneWeight.Length != vertex.WeightCount || vertex.BoneIndex.Length != vertex.WeightCount
                || vertex.BoneWeight.Any(weight => !float.IsFinite(weight) || weight < 0 || weight > 1))
                throw new VfxMappingException("RigidData/Weights", "Vfx.Preview.GeometryUnsupported");
            var weights = vertex.BoneWeight.ToArray();
            // Shipped vertex shaders reconstruct the last weight instead of reading its padding byte.
            if (vertex.WeightCount == 2) weights[1] = 1 - weights[0];
            else
            {
                var sum = weights[0] + weights[1] + weights[2];
                weights[3] = sum < 255f / 256 ? 1 - sum : 0;
            }
            if (Math.Abs(weights.Sum() - 1) > .02f)
                throw new VfxMappingException("RigidData/Weights", "Vfx.Preview.GeometryUnsupported");
            skinWeights[index] = weights;
            for (var weight = 0; weight < vertex.WeightCount; ++weight)
                if (weights[weight] > 0 && vertex.BoneIndex[weight] >= skeleton.BoneCount)
                    throw new VfxMappingException("RigidData/Weights", "Vfx.Preview.GeometryUnsupported");
        }
        var requestedFrames = cycles == 0 ? 1 : Math.Max(2, Math.Ceiling((double)maximumLife * 60 / cycles));
        if (!double.IsFinite(requestedFrames) || requestedFrames > 1800)
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.RangeUnsupported");
        var frames = (int)requestedFrames;
        var vertexCount = (long)vertices.Length * frames;
        var indexCount = (long)indices.Length * frames;
        budget.Reserve(vertexCount, indexCount);
        try
        {
            var baked = new EffekseerVertex[checked((int)vertexCount)];
            for (var frame = 0; frame < frames; ++frame)
            {
                // Native VFX uses the authored frame rate and holds any gap after the last key.
                var samplePosition = Math.Min(clip.DynamicFrames.Count - 1,
                    frame / (double)frames * animationFile.Header.FrameRate * animationFile.Header.AnimationTotalPlayTimeInSec);
                var sampleIndex = (int)Math.Floor(samplePosition);
                var pose = AnimationSampler.Sample(sampleIndex, (float)(samplePosition - sampleIndex), skeleton, clip);
                for (var index = 0; index < vertices.Length; ++index)
                {
                    var weights = source[index];
                    var transform = new Matrix();
                    for (var weight = 0; weight < weights.WeightCount; ++weight)
                        if (skinWeights[index][weight] > 0)
                            transform += pose.BoneTransforms[weights.BoneIndex[weight]].WorldTransform * skinWeights[index][weight];
                    var vertex = vertices[index];
                    vertex.Position = Vector3.Transform(vertex.Position, transform);
                    vertex.Normal = Unit(Vector3.TransformNormal(vertex.Normal, transform));
                    vertex.Tangent = Unit(Vector3.TransformNormal(vertex.Tangent, transform));
                    vertex.Binormal = Unit(Vector3.TransformNormal(vertex.Binormal, transform));
                    if (!Finite(vertex.Position)) throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
                    baked[frame * vertices.Length + index] = vertex;
                }
            }
            return new(baked.AsSpan(0, vertices.Length).ToArray(), indices, frames == 1 ? null : baked, frames, cycles);
        }
        catch { budget.Release(vertexCount, indexCount); throw; }
    }

    private static Vector3 Unit(Vector3 value)
    {
        if (!Finite(value) || value.LengthSquared() <= .00000001f)
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
        return Vector3.Normalize(value);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static AnimationFile ReadAnimation(byte[] bytes)
    {
        if (bytes.Length < 16 || BitConverter.ToUInt32(bytes) is not (7 or 8))
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
        try { return AnimationFile.Create(new PackFile("animation.anim", new MemorySource(bytes))); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        { throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData"); }
    }
}
