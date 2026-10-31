using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Vfx;

internal static class VfxLegacyModelColour
{
    internal static string ResolveEmbeddedDiffuse(RmvVersionEnum version, VertexFormat vertexFormat, ModelMaterialEnum materialId, string path)
    {
        // The native rigid loader upgrades slot 0 before looking up its resource.
        // Keep the verified static material scope separate from explicit WsModel overrides.
        const string suffix = "_diffuse.dds";
        if (version != RmvVersionEnum.RMV2_V7 || vertexFormat != VertexFormat.Static || materialId != ModelMaterialEnum.ship_ambientmap
            || !path.EndsWith(suffix, StringComparison.Ordinal)) return path;
        var index = path.Length - suffix.Length;
        // Native find uses its first ordinal match; do not reinterpret ambiguous paths.
        if (path.IndexOf("_diffuse", StringComparison.Ordinal) != index) return path;
        return path[..index] + "_base_colour.dds";
    }
}
