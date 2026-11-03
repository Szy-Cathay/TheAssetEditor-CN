using Editors.AnimationMeta.SuperView.Visualisation;

namespace Test.AnimationMeta;

public class EffectIdFilterTests
{
    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(999)]
    [TestCase(1101)]
    [TestCase(int.MaxValue)]
    public void ExactIdRanges_RequireTheRequestedVfxFilterId(int id)
    {
        Assert.That(EffectIdFilter.IsAllowed(id, id, true), Is.True);
        Assert.That(EffectIdFilter.IsAllowed(id, 0, true), Is.False);
    }

    [TestCase(0)]
    [TestCase(1100)]
    public void NativeUnconditionalIds_AreNotFilteredByARequestedIdOrAnEmptyGroupMask(int id)
        => Assert.That(EffectIdFilter.IsAllowed(id, 27, true, new byte[13]), Is.True);

    [TestCase(1000)]
    [TestCase(1031)]
    [TestCase(1032)]
    [TestCase(1063)]
    [TestCase(1064)]
    [TestCase(1099)]
    public void GroupIds_UseTheirIndependentBitAndDoNotRequireCurrentIdEquality(int id)
    {
        var index = id - 1000;
        var bits = new byte[13]; bits[index / 8] = (byte)(1 << (index % 8));
        Assert.Multiple(() =>
        {
            Assert.That(EffectIdFilter.IsAllowed(id, 27, true, bits), Is.True);
            Assert.That(EffectIdFilter.IsAllowed(id, id, true, new byte[13]), Is.False);
            Assert.That(EffectIdFilter.IsAllowed(id, 27, true), Is.True, "The native null mask does not disable group effects.");
            Assert.That(bits.Sum(value => System.Numerics.BitOperations.PopCount((uint)value)), Is.EqualTo(1));
        });
    }

    [TestCase(1)]
    [TestCase(1000)]
    [TestCase(1099)]
    [TestCase(1101)]
    public void AuthoringBypass_DoesNotApplyEitherNativeFilterGate(int id)
        => Assert.That(EffectIdFilter.IsAllowed(id, 0, false, new byte[13]), Is.True);

    [Test]
    public void IncompleteNonNullGroupMask_IsRejectedInsteadOfReadingOutsideItsBuffer()
        => Assert.Throws<ArgumentException>(() => EffectIdFilter.IsAllowed(1099, 0, true, new byte[12]));
}
