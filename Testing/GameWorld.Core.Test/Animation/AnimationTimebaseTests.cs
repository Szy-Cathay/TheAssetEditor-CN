using Shared.GameFormats.Animation;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class AnimationTimebaseTests
{
    [Test]
    public void FrameSamples_UseHalfOpenAnimationTime()
    {
        var timebase = new AnimationTimebase(
            frameCount: 5,
            duration: TimeSpan.FromMilliseconds(250));

        Assert.Multiple(() =>
        {
            Assert.That(timebase.FramesPerSecond, Is.EqualTo(20).Within(0.000001));
            Assert.That(timebase.GetSampleTime(0), Is.EqualTo(TimeSpan.Zero));
            Assert.That(timebase.GetSampleTime(1), Is.EqualTo(TimeSpan.FromMilliseconds(50)));
            Assert.That(timebase.GetSampleTime(4), Is.EqualTo(TimeSpan.FromMilliseconds(200)));
            Assert.That(timebase.GetSampleTime(4), Is.LessThan(timebase.Duration));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_RejectsNonPositiveFrameCount(int frameCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnimationTimebase(frameCount, TimeSpan.FromSeconds(1)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_RejectsNonPositiveDuration(int durationTicks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnimationTimebase(1, TimeSpan.FromTicks(durationTicks)));
    }

    [TestCase(-1)]
    [TestCase(5)]
    public void GetSampleTime_RejectsIndexOutsideHalfOpenFrameRange(int frameIndex)
    {
        var timebase = new AnimationTimebase(5, TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => timebase.GetSampleTime(frameIndex));
    }
}
