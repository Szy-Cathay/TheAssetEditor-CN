using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxCurveBoundaryTests
{
    // Unmodified ParticleScale X channels from the official storm-dragon impact_ground_wave.
    private const string ImpactScale = "x_min,8405056,1;0.0,0.0,linear,linear,-0.05,0.0,0.05,0.0,0;0.0,0.0,linear,log3,-0.05,0.0,0.05,0.0,0;1.0,22.150801,log3,linear,-0.05,0.0,0.05,0.0,0|x_max,16728128,1;0.0,0.0,linear,linear,-0.05,0.0,0.05,0.0,0;0.0,0.0,linear,log3,-0.05,0.0,0.05,0.0,0;1.0,26.564701,log3,linear,-0.05,0.0,0.05,0.0,0:x,1;0,1";

    [TestCase(0, 0)]
    [TestCase(.5, 19.381950875)]
    [TestCase(1, 22.150801)]
    public void OfficialImpactCoincidentStart_UsesItsOutgoingLogarithmicSegment(double phase, double expected)
    {
        var curve = new VfxPreviewCurve(ImpactScale, "ParticleScale", 1);
        Assert.That(curve.Sample("x", phase, 0), Is.EqualTo(expected).Within(.000002));
        Assert.That(VfxCurve.TryParse(ImpactScale, out var source), Is.True);
        Assert.That(source!.Channels.All(channel => channel.Points.Count == 3), Is.True);
        Assert.That(source.ToString(), Is.EqualTo(ImpactScale));
    }

    [TestCase(.25, .15625)]
    [TestCase(.5, .5)]
    [TestCase(.75, .9375)]
    public void CoincidentInterior_UsesFirstIncomingAndLastOutgoingModes(double phase, double expected)
    {
        const string text = "x,0,1;0,0,linear,linear,-0.05,0,0.05,0,0;0.5,0.5,exp3,linear,-0.05,0,0.05,0,0;0.5,0.5,linear,log3,-0.05,0,0.05,0,0;1,1,log3,linear,-0.05,0,0.05,0,0:x,0;0,0";
        var curve = new VfxPreviewCurve(text, "ParticleScale", 1);
        Assert.That(curve.Sample("x", phase, 0), Is.EqualTo(expected).Within(.000001));
    }

    [TestCase(.25, .1375)]
    [TestCase(.5, .5)]
    [TestCase(.75, .8625)]
    public void CoincidentInterior_UsesFirstIncomingAndLastOutgoingBezierHandles(double phase, double expected)
    {
        const string text = "x,0,1;0,0,bezier_c,bezier_c,-0.16666666666666667,0,0.16666666666666667,0,0;0.5,0.5,bezier_c,bezier_c,-0.16666666666666667,-0.3,0.16666666666666667,-0.2,0;0.5,0.5,bezier_c,bezier_c,-0.16666666666666667,0.2,0.16666666666666667,0.3,0;1,1,bezier_c,bezier_c,-0.16666666666666667,0,0.16666666666666667,0,0:x,0;0,0";
        var curve = new VfxPreviewCurve(text, "ParticleScale", 1);
        Assert.That(curve.Sample("x", phase, 0), Is.EqualTo(expected).Within(.000001));
    }

    [TestCase("0.300000000000001")]
    [TestCase("0.4")]
    public void CoincidentDifferentValues_StillFailWithoutATolerance(string value)
    {
        var text = $"x,0,1;0.5,0.3,linear,linear,-0.05,0,0.05,0,0;0.5,{value},linear,linear,-0.05,0,0.05,0,0:x,0;0,0";
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(text, "ParticleScale", 1));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }

    [Test]
    public void CoincidentInvalidHandles_AreStillRejected()
    {
        const string text = "x,0,1;0,0,linear,linear,-0.05,invalid,0.05,0,0;0,0,linear,log3,-0.05,0,0.05,0,0;1,1,log3,linear,-0.05,0,0.05,0,0:x,0;0,0";
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(text, "ParticleScale", 1));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.InvalidData"));
    }

    private const string PlanarScale = "x,0,0;0,2,linear,linear,-0.05,0,0.05,0,1|y,0,0;0,3,linear,linear,-0.05,0,0.05,0,1|z_min,0,1|z_max,0,1:x,0;0,0|y,0;1,1|z,0;2,3";

    [TestCase("ParticleScale")]
    [TestCase("ParticleInitialScale")]
    public void PlanarScale_EmptyZDefaultsToOneWithoutChangingXY(string field)
    {
        var curve = new VfxPreviewCurve(PlanarScale, field, 1, planarScale: true);
        curve.RequireChannels("x", "y", "z");
        Assert.Multiple(() =>
        {
            Assert.That(curve.Sample("x", .5, .7f), Is.EqualTo(2));
            Assert.That(curve.Sample("y", .5, .7f), Is.EqualTo(3));
            Assert.That(curve.Sample("z", .5, .7f), Is.EqualTo(1));
        });
    }

    [TestCase("ParticleScale", false)]
    [TestCase("ParticleInitialScale", false)]
    [TestCase("EmitterPosition", true)]
    public void EmptyZ_StaysInvalidForThreeDimensionalOrOtherCurves(string field, bool planarScale)
    {
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(PlanarScale, field, 1, planarScale));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }

    [Test]
    public void PlanarScale_KeepsValidZForInheritedChildScale()
    {
        var text = PlanarScale.Replace("z_min,0,1|z_max,0,1", "z_min,0,0;0,4,linear,linear,-0.05,0,0.05,0,1|z_max,0,0;0,8,linear,linear,-0.05,0,0.05,0,1");
        var curve = new VfxPreviewCurve(text, "ParticleScale", 1, planarScale: true);
        Assert.That(curve.Sample("z", .5, .25f), Is.EqualTo(5));
    }

    [Test]
    public void PlanarScale_StillRejectsEmptyX()
    {
        var text = PlanarScale.Replace("x,0,0;0,2,linear,linear,-0.05,0,0.05,0,1", "x,0,1");
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(text, "ParticleScale", 1, planarScale: true));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }
}
