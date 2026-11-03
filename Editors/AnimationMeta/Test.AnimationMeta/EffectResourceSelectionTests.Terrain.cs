using Editors.AnimationMeta.SuperView.Visualisation;

namespace Test.AnimationMeta;

public partial class EffectResourceSelectionTests
{
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(5, 5)]
    [TestCase(6, 6)]
    [TestCase(7, 7)]
    [TestCase(8, 8)]
    [TestCase(9, 9)]
    [TestCase(10, 10)]
    [TestCase(11, 5)]
    [TestCase(12, 1)]
    [TestCase(13, 2)]
    [TestCase(14, 1)]
    [TestCase(15, 0)]
    public void TerrainRoute_UsesNativeCategoryAndPreservesTheResolvedResource(int rawTerrain, int category)
    {
        var value = new object();
        var resolved = -1;
        var selected = EffectResourceSelection.Select(true, rawTerrain, false, 2,
            () => throw new InvalidOperationException("Terrain selection must not load the authored fallback."), new object(), new object(), index =>
            {
                resolved = index;
                return value;
            });
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.EqualTo(category));
            Assert.That(selected, Is.SameAs(value));
        });
    }

    [TestCase(-1)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void UnknownTerrain_SkipsWithoutResolvingAnyResource(int rawTerrain)
    {
        var selected = EffectResourceSelection.Select(true, rawTerrain, false, 0,
            () => throw new InvalidOperationException("Unknown terrain has no authored fallback."), new object(), new object(),
            _ => throw new InvalidOperationException("The sentinel must not index terrain resources."));
        Assert.That(selected, Is.Null);
    }

    [TestCase("footstep_forest", "footstep")]
    [TestCase("footstep_grass", "footstep")]
    [TestCase("footstep_deep_water", "footstep")]
    [TestCase("footstep_shallow_water", "footstep")]
    [TestCase("footstep_wooden_floor", "footstep")]
    [TestCase("footstep_sharp_stones", "footstep")]
    [TestCase("footstep_wet_mud", "footstep_wet")]
    [TestCase("footstep_long_grass", "footstep_long")]
    [TestCase("footstep_light_forest", "footstep_light")]
    [TestCase("footstep_Grass", "footstep_Grass")]
    [TestCase("footstep_grass.xml", "footstep_grass.xml")]
    [TestCase("_grass", "_grass")]
    [TestCase("forest", "forest")]
    [TestCase("footstep_grass_grass", "footstep_grass")]
    [TestCase("footstep_unknown", "footstep_unknown")]
    [TestCase("", "")]
    public void TerrainDbKey_RemovesOnlyFirstExactSuffixInNativeEnumOrder(string authoredName, string expectedKey)
    {
        Assert.That(EffectResourceSelection.GetTerrainKey(authoredName), Is.EqualTo(expectedKey));
    }
}
