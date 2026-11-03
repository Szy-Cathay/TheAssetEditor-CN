using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

[StructLayout(LayoutKind.Sequential)]
internal struct EffekseerSkinWeight
{
    public int Bone0, Bone1, Bone2, Bone3;
    public Vector4 Weights;
}

// Keep the existing pose samples, but materialize only frames which are actually drawn.
internal sealed class VfxModelAnimationData(EffekseerSkinWeight[] weights, Matrix[] poses, int boneCount, int frameCount, Vector3 offset = default)
{
    internal const int CachedFrames = 4, MaximumFrames = 65536;
    internal EffekseerSkinWeight[] Weights { get; } = weights;
    internal Matrix[] Poses { get; } = poses;
    internal int BoneCount { get; } = boneCount;
    internal int FrameCount { get; } = frameCount;
    internal Vector3 Offset { get; } = offset;
    private readonly ConcurrentDictionary<int, BoundingBox> _bounds = new();

    internal static long ReservedVertices(int vertices, int frames, int bones)
    {
        var bytes = checked((long)vertices * 72 * (CachedFrames + 1) + (long)vertices * 32
            + (long)frames * bones * 64 + (long)frames * 128);
        return (bytes + 71) / 72;
    }

    internal EffekseerVertex[] Sample(EffekseerVertex[] vertices, int frame)
    {
        frame = ((frame % FrameCount) + FrameCount) % FrameCount;
        var result = new EffekseerVertex[vertices.Length];
        for (var index = 0; index < vertices.Length; ++index)
        {
            var skin = Weights[index];
            var offset = frame * BoneCount;
            var transform = new Matrix();
            if (skin.Weights.X > 0) transform += Poses[offset + skin.Bone0] * skin.Weights.X;
            if (skin.Weights.Y > 0) transform += Poses[offset + skin.Bone1] * skin.Weights.Y;
            if (skin.Weights.Z > 0) transform += Poses[offset + skin.Bone2] * skin.Weights.Z;
            if (skin.Weights.W > 0) transform += Poses[offset + skin.Bone3] * skin.Weights.W;
            var vertex = vertices[index];
            vertex.Position = Vector3.Transform(vertex.Position, transform) + Offset;
            if (!float.IsFinite(vertex.Position.X) || !float.IsFinite(vertex.Position.Y) || !float.IsFinite(vertex.Position.Z))
                throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
            vertex.Normal = Unit(Vector3.TransformNormal(vertex.Normal, transform));
            vertex.Tangent = Unit(Vector3.TransformNormal(vertex.Tangent, transform));
            vertex.Binormal = Unit(Vector3.TransformNormal(vertex.Binormal, transform));
            result[index] = vertex;
        }
        return result;
    }

    internal BoundingBox Bounds(EffekseerVertex[] vertices, int frame)
    {
        if (_bounds.Count >= 64) _bounds.Clear();
        return _bounds.GetOrAdd(frame, value => BoundingBox.CreateFromPoints(Sample(vertices, value).Select(vertex => vertex.Position)));
    }

    private static Vector3 Unit(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || value.LengthSquared() <= .00000001f)
            throw new VfxMappingException("RigidData/Animation", "Vfx.Preview.InvalidData");
        return Vector3.Normalize(value);
    }
}
