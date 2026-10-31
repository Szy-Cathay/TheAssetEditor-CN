using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GameWorld.Core.Vfx;

// Resource slots and upload payloads are immutable within a native playback handle.
internal static class VfxPlaybackResourceIdentity
{
    // A completed diffuse-only model fallback keeps its original shader identity and diagnostic.
    internal static bool CanReuse(VfxConversion conversion) => !conversion.Diagnostics.Any(diagnostic => diagnostic.ReasonKey is
        "Vfx.Preview.MissingResource" or "Vfx.Preview.InvalidData" or "Vfx.Preview.GeometryUnsupported"
        or "Vfx.Preview.MaterialUnsupported" or "Vfx.Preview.TextureUnsupported"
        or "Vfx.Preview.ShadingNormalUnsupported" or "Vfx.Preview.LayerTextureUnavailable");

    internal static string Create(VfxConversion conversion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append<int>(hash, [conversion.Textures.Count]);
        for (var index = 0; index < conversion.Textures.Count; index++)
        {
            Append<int>(hash, [(int)conversion.TextureUsages[index]]);
            Append<byte>(hash, Encoding.UTF8.GetBytes(conversion.TexturePaths[index]));
            Append<byte>(hash, conversion.Textures[index]);
        }
        Append<int>(hash, [conversion.Models.Count]);
        foreach (var model in conversion.Models)
        {
            Append<EffekseerVertex>(hash, model.Vertices);
            Append<int>(hash, model.Indices);
            Append<int>(hash, [model.AnimationVertices == null ? 0 : 1, model.AnimationFrameCount,
                model.OpaqueMaterial ? 1 : 0, model.RigidAnimation ? 1 : 0, model.ApproximateMaterialShader == null ? 0 : 1]);
            Append<float>(hash, [model.AnimationCycles]);
            Append<EffekseerVertex>(hash, model.AnimationVertices ?? []);
            Append<byte>(hash, Encoding.UTF8.GetBytes(model.ApproximateMaterialShader ?? ""));
        }
        Append<int>(hash, [conversion.MeshMaterials.Count]);
        foreach (var material in conversion.MeshMaterials)
        {
            Append<int>(hash, material.TextureIndexes);
            Append<float>(hash, material.Parameters);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append<T>(IncrementalHash hash, ReadOnlySpan<T> values) where T : unmanaged
    {
        ReadOnlySpan<int> length = [values.Length];
        hash.AppendData(MemoryMarshal.AsBytes(length));
        hash.AppendData(MemoryMarshal.AsBytes(values));
    }
}
