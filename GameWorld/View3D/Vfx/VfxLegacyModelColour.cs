using Shared.GameFormats.RigidModel;

namespace GameWorld.Core.Vfx;

internal static class VfxLegacyModelColour
{
    internal static string ResolveEmbeddedDiffuse(RmvVersionEnum version, VertexFormat vertexFormat, ModelMaterialEnum materialId, string path)
    {
        // The native rigid loader upgrades slot 0 before looking up its resource.
        // Static and weighted native mesh constructors share this importer; explicit WsModel overrides remain separate.
        const string token = "_diffuse";
        if (version is not (RmvVersionEnum.RMV2_V5 or RmvVersionEnum.RMV2_V6 or RmvVersionEnum.RMV2_V7 or RmvVersionEnum.RMV2_V8)
            || !(vertexFormat == VertexFormat.Static && materialId is ModelMaterialEnum.ship_ambientmap or ModelMaterialEnum.default_type
                || vertexFormat is VertexFormat.Weighted or VertexFormat.Cinematic && materialId == ModelMaterialEnum.weighted)) return path;
        var index = path.IndexOf(token, StringComparison.Ordinal);
        var upgraded = index < 0 ? path : path[..index] + "_base_colour" + path[(index + token.Length)..];
        return VfxDefaultTexture.Resolve(upgraded, srgb: true);
    }
}
