using Editors.AnimationMeta.SuperView.Visualisation;

namespace Test.AnimationMeta;

public class EffectDistanceCullGateTests
{
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void CullRequiresBothTheAuthoredFlagAndTheNativeActorCondition(bool effectFlag, bool actorLodCondition, bool expected)
        => Assert.That(EffectDistanceCullGate.ShouldCull(effectFlag, actorLodCondition), Is.EqualTo(expected));

    [TestCase(1)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void AnyNonzeroNativeOverrideBypassesTheCullGate(int overrideCount)
        => Assert.That(EffectDistanceCullGate.ShouldCull(true, true, overrideCount), Is.False);

    [Test]
    public void ActorConditionCanRecoverWithoutChangingTheAuthoredFlagOrTheOverride()
    {
        const bool effectFlag = true;
        const int overrideCount = 0;
        var conditions = new[] { false, true, true, false, true, false };
        Assert.That(conditions.Select(condition => EffectDistanceCullGate.ShouldCull(effectFlag, condition, overrideCount)),
            Is.EqualTo(new[] { false, true, true, false, true, false }));
    }
}
