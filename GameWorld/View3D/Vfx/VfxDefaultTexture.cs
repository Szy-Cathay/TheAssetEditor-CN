namespace GameWorld.Core.Vfx;

internal static class VfxDefaultTexture
{
    private static readonly (string Token, string Resource)[] Defaults =
    [
        ("test_gray", "grey"), ("test_black", "black"), ("test_mask", "black"),
        ("test_white", "white"), ("test_gloss", "black"),
        ("flatnormal", "normal"), ("test_material_map", "material_map"),
    ];

    // Native rigid material loader52d36e: ordered ordinal path-substring substitutions.
    internal static string Resolve(string path, bool srgb)
    {
        string? name = null;
        foreach (var (token, resource) in Defaults)
        {
            if (!path.Contains("/" + token, StringComparison.Ordinal) && !path.Contains("\\" + token, StringComparison.Ordinal)) continue;
            name = resource;
            break;
        }
        if (name == null) return path;
        var suffix = srgb && name is not ("normal" or "material_map") ? "_srgb" : "";
        return "commontextures/default_" + name + suffix + ".dds";
    }
}
