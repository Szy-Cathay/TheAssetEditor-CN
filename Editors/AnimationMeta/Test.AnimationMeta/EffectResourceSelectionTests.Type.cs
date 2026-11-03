using Editors.AnimationMeta.SuperView.Visualisation;
using Shared.Core.PackFiles.Models;

namespace Test.AnimationMeta;

public partial class EffectResourceSelectionTests
{
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(3)]
    public void NormalOrUnrecognizedType_PreservesTheAuthoredResourceIdentity(int effectType)
    {
        var authored = PackFile.CreateFromBytes("vfx/same.xml", [1]);
        var aura = PackFile.CreateFromBytes("vfx/same.xml", [2]);
        var weapon = PackFile.CreateFromBytes("vfx/same.xml", [3]);
        var selected = EffectResourceSelection.Select(false, 0, false, effectType, () => authored, aura, weapon,
            _ => throw new InvalidOperationException("An inactive terrain route must not resolve a variant."));
        Assert.That(selected, Is.SameAs(authored));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void AbilityType_SelectsOnlyItsOwnNonNullOverrideAndRetainsTheExactPackFile(int effectType)
    {
        var aura = PackFile.CreateFromBytes("vfx/same.xml", [2]);
        var weapon = PackFile.CreateFromBytes("vfx/same.xml", [3]);
        var selected = EffectResourceSelection.Select(false, 0, false, effectType,
            () => throw new InvalidOperationException("A supplied override must not resolve the unused authored resource."), aura, weapon,
            _ => throw new InvalidOperationException("Type overrides must not consult the terrain route."));
        Assert.That(selected, Is.SameAs(effectType == 1 ? aura : weapon), "Equal resource names must not collapse distinct Pack sources.");
    }

    [TestCase(1)]
    [TestCase(2)]
    public void MissingRequestedOverride_FallsBackToAuthoredAndIgnoresTheOtherAbilityType(int effectType)
    {
        var authored = new object(); var unrelated = new object();
        var selected = EffectResourceSelection.Select(false, 0, false, effectType, () => authored,
            effectType == 2 ? unrelated : null, effectType == 1 ? unrelated : null, _ => null);
        Assert.That(selected, Is.SameAs(authored));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ValidOverride_DoesNotRequireAnAuthoredResource(int effectType)
    {
        var value = new object();
        var selected = EffectResourceSelection.Select(false, 0, false, effectType, () => null, value, value, _ => null);
        Assert.That(selected, Is.SameAs(value));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void SpecialCallerBypassesTerrainAndStillUsesTheAbilityTypeOverride(int effectType)
    {
        var authored = new object(); var aura = new object(); var weapon = new object();
        var selected = EffectResourceSelection.Select(true, int.MaxValue, true, effectType, () => authored, aura, weapon,
            _ => throw new InvalidOperationException("The native special caller bypasses terrain classification entirely."));
        Assert.That(selected, Is.SameAs(effectType == 1 ? aura : weapon));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ActiveTerrainRoute_IsExclusiveAndDoesNotApplyAnAbilityOverride(int effectType)
    {
        var aura = new object(); var weapon = new object(); var terrain = new object();
        var resolutions = 0;
        var selected = EffectResourceSelection.Select(true, 0, false, effectType,
            () => throw new InvalidOperationException("A terrain variant must not resolve the unused authored resource."), aura, weapon, _ =>
        {
            resolutions++;
            return terrain;
        });
        Assert.Multiple(() =>
        {
            Assert.That(resolutions, Is.EqualTo(1));
            Assert.That(selected, Is.SameAs(terrain));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void NullTerrainVariant_DoesNotFallBackToAuthoredOrAbilityResources(int effectType)
    {
        var selected = EffectResourceSelection.Select(true, 0, false, effectType,
            () => throw new InvalidOperationException("A missing terrain variant must not resolve the authored resource."), new object(), new object(), _ => null);
        Assert.That(selected, Is.Null);
    }
}
