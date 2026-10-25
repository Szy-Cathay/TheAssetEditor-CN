using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxCurveSamplingCostTests
{
    [Test]
    public void RepeatedSampling_DoesNotAllocateTextParsingOrChannelSearchObjects()
    {
        var curve = new VfxPreviewCurve("x_min,1,0;0,1,linear,linear,0,0,0,0,1;1,3,linear,linear,0,0,0,0,1|x_max,2,0;0,3,linear,linear,0,0,0,0,1;1,5,linear,linear,0,0,0,0,1:x,1;0,1", "ParticleScale", 1);
        for (var i = 0; i < 1000; i++) curve.Sample("x", .25, .3f);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        for (var i = 0; i < 10000; i++) total += curve.Sample("x", .25, .3f);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(21000).Within(.01));
            Assert.That(allocated, Is.LessThan(32768), "Repeated numeric curve sampling must not create per-sample text/search allocations.");
        });
    }

    [Test]
    public void OverlappingScalarAndMinimumChannels_KeepOriginalFirstMatchPrecedence()
    {
        var curve = new VfxPreviewCurve("x,1,0;0,2,linear,linear,0,0,0,0,1|x_min,2,0;0,9,linear,linear,0,0,0,0,1|x_max,3,0;0,4,linear,linear,0,0,0,0,1:x,1;0,2", "ParticleInitialScale", 1);
        Assert.That(curve.Range("x", .5), Is.EqualTo(new VfxRange(2, 4)));
        Assert.That(curve.Range("y", .5), Is.EqualTo(new VfxRange(1, 1)));
    }

    [Test]
    public void AConstantKey_DoesNotRequireUnusedTangentNumbers()
    {
        var curve = new VfxPreviewCurve("x,1,0;0,2,linear,linear,unused,0,unused,0,1:x,1;0,0", "ParticleScale", 1);
        Assert.That(curve.Sample("x", .5, .3f), Is.EqualTo(2));
    }
}
