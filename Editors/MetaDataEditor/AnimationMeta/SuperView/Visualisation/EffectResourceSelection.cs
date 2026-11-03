namespace Editors.AnimationMeta.SuperView.Visualisation;

internal static class EffectResourceSelection
{
    private static readonly string[] TerrainTokens =
    [
        "forest", "grass", "mud", "sand", "scrub", "rock", "deep_water", "shallow_water",
        "road", "wooden_floor", "snow", "sharp_stones", "burnt", "wet_mud", "long_grass", "light_forest"
    ];

    internal static int ClassifyTerrain(int terrainType) => terrainType switch
    {
        >= 0 and <= 10 => terrainType,
        11 => 5,
        12 or 14 => 1,
        13 => 2,
        15 => 0,
        _ => 16
    };

    internal static string GetTerrainKey(string vfxName)
    {
        // Native scans this order and removes only the first exact trailing token.
        foreach (var token in TerrainTokens)
        {
            var suffix = "_" + token;
            if (vfxName.Length > suffix.Length && vfxName.EndsWith(suffix, StringComparison.Ordinal))
                return vfxName[..^suffix.Length];
        }

        return vfxName;
    }

    internal static T? Select<T>(bool terrainMutable, int terrainType, bool bypassTerrain, int effectType,
        Func<T?> authored, T? aura, T? weapon, Func<int, T?> terrainResource) where T : class
    {
        if (terrainMutable && !bypassTerrain)
        {
            var category = ClassifyTerrain(terrainType);
            return category == 16 ? null : terrainResource(category);
        }

        return effectType switch
        {
            1 => aura ?? authored(),
            2 => weapon ?? authored(),
            _ => authored()
        };
    }
}
