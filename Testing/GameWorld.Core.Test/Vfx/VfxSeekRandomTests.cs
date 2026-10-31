using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSeekRandomTests
{
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(1940)]
    [TestCase(int.MaxValue)]
    public void RecordedApis_KeepEveryRuntimeRandomBit(int seed)
    {
        var original = new Random(seed);
        var recorded = VfxSeekRandom.Create(seed, new(1024 * 1024));
        for (var index = 0; index < 100; index++) AssertCallsEqual(recorded, original);
    }

    [Test]
    public void RestoredCursor_ReusesFutureValuesAndCanContinuePastTheRecordedEnd()
    {
        var recorded = (VfxSeekRandom)VfxSeekRandom.Create(1940, new(1024 * 1024));
        var original = new Random(1940);
        for (var index = 0; index < 7; index++) AssertCallsEqual(recorded, original);
        var checkpoint = recorded.CloneCursor();
        for (var index = 0; index < 20; index++) AssertCallsEqual(recorded, original);
        var replayOriginal = new Random(1940);
        for (var index = 0; index < 7; index++) AssertCallsEqual(new Random(), replayOriginal, compare: false);
        for (var index = 0; index < 30; index++) AssertCallsEqual(checkpoint, replayOriginal);
    }

    [Test]
    public void DifferentApiAfterRewind_ForksUsingTheRealSeededRuntime()
    {
        var stream = (VfxSeekRandom)VfxSeekRandom.Create(7919, new(1024 * 1024));
        var original = new Random(7919);
        for (var index = 0; index < 11; index++) Assert.That(stream.NextSingle(), Is.EqualTo(original.NextSingle()));
        var fork = stream.CloneCursor();
        for (var index = 0; index < 15; index++) stream.NextSingle();
        Assert.That(fork.NextInt64(-9000000000, 9000000000), Is.EqualTo(original.NextInt64(-9000000000, 9000000000)));
        for (var index = 0; index < 50; index++) AssertCallsEqual(fork, original);
    }

    [Test]
    public void BudgetExhaustion_AndDetachFromAnEarlierCursor_PreserveTheRemainingStream()
    {
        var budget = new VfxSeekRandomBudget(5000);
        var stream = (VfxSeekRandom)VfxSeekRandom.Create(17, budget);
        var original = new Random(17);
        for (var index = 0; index < 12; index++) AssertCallsEqual(stream, original);
        var checkpoint = stream.CloneCursor();
        for (var index = 0; index < 100; index++) AssertCallsEqual(stream, original);
        Assert.That(budget.Enabled, Is.False);
        Assert.That(budget.UsedBytes, Is.LessThanOrEqualTo(5000));
        var rewindOriginal = new Random(17);
        for (var index = 0; index < 12; index++) AssertCallsEqual(new Random(), rewindOriginal, compare: false);
        var detached = checkpoint.DetachExact();
        for (var index = 0; index < 100; index++) AssertCallsEqual(detached, rewindOriginal);
    }

    [Test]
    public void BytesAfterRewind_MaterializeTheExactRuntimeState()
    {
        var budget = new VfxSeekRandomBudget(1024 * 1024);
        var stream = (VfxSeekRandom)VfxSeekRandom.Create(-17, budget);
        var original = new Random(-17);
        AssertCallsEqual(stream, original);
        var checkpoint = stream.CloneCursor();
        for (var index = 0; index < 10; index++) stream.NextDouble();
        var actual = new byte[257]; var expected = new byte[257];
        checkpoint.NextBytes(actual.AsSpan()); original.NextBytes(expected.AsSpan());
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(budget.Enabled, Is.False);
        AssertCallsEqual(checkpoint, original);
    }

    private static void AssertCallsEqual(Random actual, Random expected, bool compare = true)
    {
        long[] Sample(Random random) => [random.Next(), random.Next(257), random.Next(-713, 917),
            BitConverter.SingleToInt32Bits(random.NextSingle()), BitConverter.DoubleToInt64Bits(random.NextDouble()),
            random.NextInt64(), random.NextInt64(17000000000), random.NextInt64(-100000000000, 230000000000)];
        var values = Sample(actual); var control = Sample(expected);
        if (compare) Assert.That(values, Is.EqualTo(control));
    }
}
